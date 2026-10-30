using System.Reflection;
using RecompOne.Runtime.Context;
using RecompOne.Runtime.Memory;

namespace NascarRumble.Host;

/// <summary>
/// Differential checker for recompiled functions. Before the recompiled C# runs, the original
/// MIPS code (still resident in emulated RAM) is interpreted over the same CPU/GTE/RAM state.
/// Writes go to a private overlay and the GTE is restored afterwards, so the real execution is
/// unaffected. After the recompiled function returns, its V0/V1 and memory writes are compared
/// with the interpreter's. Enable with RUMBLE_SHADOW=1.
/// </summary>
public static class ShadowCpu
{
    public static readonly bool Enabled = Environment.GetEnvironmentVariable("RUMBLE_SHADOW") == "1";

    private const int Budget = 4_000_000;

    private sealed class Pending
    {
        public required uint Entry;
        public required uint[] ArgRegs;
        public required ShadowResult Accurate;
        public required ShadowResult NoDelay;
        public required ShadowResult PrivateGte;
    }

    public sealed class ShadowResult
    {
        public uint V0, V1;
        public readonly Dictionary<uint, byte> Writes = new();
        public string? Abort;
        public long Steps;
    }

    [ThreadStatic] private static Stack<Pending>? _pending;
    private static readonly Dictionary<uint, (long Calls, long Mismatches)> Stats = new();
    private static int _reportedMismatches;

    public static void Pre(uint entry, CpuContext c, IMemory m)
    {
        if (!Enabled) return;
        _pending ??= new Stack<Pending>();
        var gte = SnapshotGte();
        var accurate = Run(entry, c, m, loadDelay: true);
        RestoreGte(gte);
        var noDelay = Run(entry, c, m, loadDelay: false);
        RestoreGte(gte);
        var privateGte = Run(entry, c, m, loadDelay: true, privateGte: true);
        RestoreGte(gte);
        _pending.Push(new Pending
        {
            Entry = entry,
            ArgRegs = [c.A0, c.A1, c.A2, c.A3, c.RA, c.SP],
            Accurate = accurate,
            NoDelay = noDelay,
            PrivateGte = privateGte,
        });
    }

    public static void Post(uint entry, CpuContext c, IMemory m)
    {
        if (!Enabled || _pending == null || _pending.Count == 0) return;
        var p = _pending.Pop();
        if (p.Entry != entry) return;

        string? accDiff = Compare(p.Accurate, c, m);
        string? rawDiff = Compare(p.NoDelay, c, m);
        string? gteDiff = Compare(p.PrivateGte, c, m);
        lock (Stats)
        {
            Stats.TryGetValue(entry, out var s);
            s.Calls++;
            if (accDiff != null || rawDiff != null || gteDiff != null) s.Mismatches++;
            Stats[entry] = s;
            if (s.Calls % 20000 == 0)
                Console.Error.WriteLine($"[shadow] {entry:x8}: calls={s.Calls} mismatches={s.Mismatches}");
        }
        if ((accDiff != null || rawDiff != null || gteDiff != null) && _reportedMismatches++ < 40)
        {
            var a = p.ArgRegs;
            Console.Error.WriteLine(
                $"[shadow] MISMATCH {entry:x8} a0={a[0]:x8} a1={a[1]:x8} a2={a[2]:x8} a3={a[3]:x8} "
                + $"ra={a[4]:x8} sp={a[5]:x8} nativeV0={c.V0:x8}");
            Console.Error.WriteLine($"[shadow]   hw-accurate(load delay): {accDiff ?? "match"} steps={p.Accurate.Steps} abort={p.Accurate.Abort ?? "-"}");
            Console.Error.WriteLine($"[shadow]   no-load-delay:           {rawDiff ?? "match"} steps={p.NoDelay.Steps} abort={p.NoDelay.Abort ?? "-"}");
            Console.Error.WriteLine($"[shadow]   private-gte(OP only):    {gteDiff ?? "match"} steps={p.PrivateGte.Steps} abort={p.PrivateGte.Abort ?? "-"}");
        }
    }

    private static string? Compare(ShadowResult r, CpuContext c, IMemory m)
    {
        if (r.Abort != null) return r.Abort.StartsWith("MiniGte") ? null : "aborted: " + r.Abort;
        var parts = new List<string>();
        if (r.V0 != c.V0) parts.Add($"V0 shadow={r.V0:x8} native={c.V0:x8}");
        int diffs = 0;
        var samples = new List<string>();
        foreach (var (addr, val) in r.Writes.OrderBy(kv => kv.Key))
        {
            byte native = m.ReadU8(0x80000000u | addr);
            if (native == val) continue;
            diffs++;
            if (samples.Count < 12) samples.Add($"{0x80000000u | addr:x8}:s={val:x2}/n={native:x2}");
        }
        if (diffs > 0) parts.Add($"{diffs} bytes differ [{string.Join(' ', samples)}]");
        return parts.Count == 0 ? null : string.Join("; ", parts);
    }

    // ------------------------------------------------------------------------------------
    // GTE state save/restore (RecompOne keeps the GTE as static fields).

    private static readonly FieldInfo[] GteFields = typeof(RecompOne.Runtime.Gte)
        .GetFields(BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public)
        .Where(f => !f.IsLiteral && f.Name != "Unr")
        .ToArray();

    internal static object?[] SnapshotGte()
    {
        var snap = new object?[GteFields.Length];
        for (int i = 0; i < GteFields.Length; i++)
        {
            var v = GteFields[i].GetValue(null);
            snap[i] = v is Array arr ? arr.Clone() : v;
        }
        return snap;
    }

    internal static void RestoreGte(object?[] snap)
    {
        for (int i = 0; i < GteFields.Length; i++)
        {
            var f = GteFields[i];
            if (snap[i] is Array src && f.GetValue(null) is Array dst) Array.Copy(src, dst, src.Length);
            else if (!f.IsInitOnly) f.SetValue(null, snap[i]);
        }
    }

    // ------------------------------------------------------------------------------------
    // R3000A interpreter over an overlay.

    private sealed class Abort(string why) : Exception(why);

    private static ShadowResult Run(uint entry, CpuContext ctx, IMemory m, bool loadDelay, bool privateGte = false)
    {
        var res = new ShadowResult();
        var r = new uint[32];
        for (int i = 1; i < 32; i++) r[i] = ctx[i];
        uint hi = ctx.HI, lo = ctx.LO;
        uint ret = ctx.RA;
        uint pc = entry, npc = entry + 4;
        int pendReg = 0; uint pendVal = 0;
        var ov = res.Writes;
        var g = privateGte ? new MiniGte() : null;
        uint GRead(int reg) => g != null ? g.Read(reg) : RecompOne.Runtime.Gte.Read(reg);
        uint GReadC(int reg) => g != null ? g.ReadControl(reg) : RecompOne.Runtime.Gte.ReadControl(reg);
        void GWrite(int reg, uint v) { if (g != null) g.Write(reg, v); else RecompOne.Runtime.Gte.Write(reg, v); }
        void GWriteC(int reg, uint v) { if (g != null) g.WriteControl(reg, v); else RecompOne.Runtime.Gte.WriteControl(reg, v); }
        void GExec(uint cmd) { if (g != null) g.Execute(cmd); else RecompOne.Runtime.Gte.Execute(cmd); }

        uint Phys(uint a)
        {
            uint p = a & 0x1FFFFFFFu;
            if (p < 0x00800000u) return p & 0x001FFFFFu;
            if (p >= 0x1F800000u && p < 0x1F800400u) return p;
            throw new Abort($"I/O access {a:x8}");
        }
        byte R8(uint a) { uint p = Phys(a); return ov.TryGetValue(p, out var b) ? b : m.ReadU8(0x80000000u | p); }
        uint R16(uint a) { if ((a & 1) != 0) throw new Abort($"unaligned lh {a:x8}"); return (uint)(R8(a) | (R8(a + 1) << 8)); }
        uint R32(uint a) { if ((a & 3) != 0) throw new Abort($"unaligned lw {a:x8}"); return (uint)(R8(a) | (R8(a + 1) << 8) | (R8(a + 2) << 16) | (R8(a + 3) << 24)); }
        void W8(uint a, uint v) => ov[Phys(a)] = (byte)v;
        void W16(uint a, uint v) { if ((a & 1) != 0) throw new Abort($"unaligned sh {a:x8}"); W8(a, v); W8(a + 1, v >> 8); }
        void W32(uint a, uint v) { if ((a & 3) != 0) throw new Abort($"unaligned sw {a:x8}"); W8(a, v); W8(a + 1, v >> 8); W8(a + 2, v >> 16); W8(a + 3, v >> 24); }

        try
        {
            long steps = 0;
            while (true)
            {
                if (++steps > Budget) throw new Abort("instruction budget exceeded");
                uint ins = R32(pc);
                uint curPc = pc;
                pc = npc; npc = pc + 4;

                int prevReg = pendReg; uint prevVal = pendVal;
                pendReg = 0;
                int wrote = 0; // gpr written directly by this instruction
                int newLoadReg = 0; uint newLoadVal = 0;

                uint op = ins >> 26, rs = (ins >> 21) & 31, rt = (ins >> 16) & 31, rd = (ins >> 11) & 31;
                uint sh = (ins >> 6) & 31, fn = ins & 63;
                uint imm = ins & 0xFFFF; uint simm = (uint)(short)imm;
                uint vs = r[rs], vt = r[rt];

                void Set(uint reg, uint v) { if (reg != 0) { r[reg] = v; wrote = (int)reg; } }
                void Load(uint reg, uint v)
                {
                    if (reg == 0) return;
                    if (loadDelay) { newLoadReg = (int)reg; newLoadVal = v; }
                    else { r[reg] = v; wrote = (int)reg; }
                }
                void Branch(bool cond) { if (cond) npc = pc + (simm << 2); }

                switch (op)
                {
                    case 0x00:
                        switch (fn)
                        {
                            case 0x00: Set(rd, vt << (int)sh); break;
                            case 0x02: Set(rd, vt >> (int)sh); break;
                            case 0x03: Set(rd, (uint)((int)vt >> (int)sh)); break;
                            case 0x04: Set(rd, vt << (int)(vs & 31)); break;
                            case 0x06: Set(rd, vt >> (int)(vs & 31)); break;
                            case 0x07: Set(rd, (uint)((int)vt >> (int)(vs & 31))); break;
                            case 0x08: npc = vs; break;
                            case 0x09: Set(rd, pc + 4); npc = vs; break;
                            case 0x0C: throw new Abort($"syscall at {curPc:x8}");
                            case 0x0D: throw new Abort($"break at {curPc:x8}");
                            case 0x10: Set(rd, hi); break;
                            case 0x11: hi = vs; break;
                            case 0x12: Set(rd, lo); break;
                            case 0x13: lo = vs; break;
                            case 0x18: { long p = (long)(int)vs * (int)vt; lo = (uint)p; hi = (uint)(p >> 32); break; }
                            case 0x19: { ulong p = (ulong)vs * vt; lo = (uint)p; hi = (uint)(p >> 32); break; }
                            case 0x1A:
                                if (vt == 0) { hi = vs; lo = (int)vs >= 0 ? 0xFFFFFFFFu : 1u; }
                                else if (vs == 0x80000000u && vt == 0xFFFFFFFFu) { lo = 0x80000000u; hi = 0; }
                                else { lo = (uint)((int)vs / (int)vt); hi = (uint)((int)vs % (int)vt); }
                                break;
                            case 0x1B:
                                if (vt == 0) { hi = vs; lo = 0xFFFFFFFFu; }
                                else { lo = vs / vt; hi = vs % vt; }
                                break;
                            case 0x20: case 0x21: Set(rd, vs + vt); break;
                            case 0x22: case 0x23: Set(rd, vs - vt); break;
                            case 0x24: Set(rd, vs & vt); break;
                            case 0x25: Set(rd, vs | vt); break;
                            case 0x26: Set(rd, vs ^ vt); break;
                            case 0x27: Set(rd, ~(vs | vt)); break;
                            case 0x2A: Set(rd, (int)vs < (int)vt ? 1u : 0u); break;
                            case 0x2B: Set(rd, vs < vt ? 1u : 0u); break;
                            default: throw new Abort($"special fn {fn:x2} at {curPc:x8}");
                        }
                        break;
                    case 0x01:
                        {
                            bool cond = (rt & 1) != 0 ? (int)vs >= 0 : (int)vs < 0;
                            if ((rt & 0x1E) == 0x10) Set(31, pc + 4);
                            Branch(cond);
                            break;
                        }
                    case 0x02: npc = (pc & 0xF0000000u) | ((ins & 0x03FFFFFFu) << 2); break;
                    case 0x03: Set(31, pc + 4); npc = (pc & 0xF0000000u) | ((ins & 0x03FFFFFFu) << 2); break;
                    case 0x04: Branch(vs == vt); break;
                    case 0x05: Branch(vs != vt); break;
                    case 0x06: Branch((int)vs <= 0); break;
                    case 0x07: Branch((int)vs > 0); break;
                    case 0x08: case 0x09: Set(rt, vs + simm); break;
                    case 0x0A: Set(rt, (int)vs < (int)simm ? 1u : 0u); break;
                    case 0x0B: Set(rt, vs < simm ? 1u : 0u); break;
                    case 0x0C: Set(rt, vs & imm); break;
                    case 0x0D: Set(rt, vs | imm); break;
                    case 0x0E: Set(rt, vs ^ imm); break;
                    case 0x0F: Set(rt, imm << 16); break;
                    case 0x12:
                        if ((ins & (1u << 25)) != 0) { GExec(ins); break; }
                        switch (rs)
                        {
                            case 0: Load(rt, GRead((int)rd)); break;
                            case 2: Load(rt, GReadC((int)rd)); break;
                            case 4: GWrite((int)rd, vt); break;
                            case 6: GWriteC((int)rd, vt); break;
                            default: throw new Abort($"cop2 rs {rs} at {curPc:x8}");
                        }
                        break;
                    case 0x20: Load(rt, (uint)(sbyte)R8(vs + simm)); break;
                    case 0x21: Load(rt, (uint)(short)R16(vs + simm)); break;
                    case 0x23: Load(rt, R32(vs + simm)); break;
                    case 0x24: Load(rt, R8(vs + simm)); break;
                    case 0x25: Load(rt, R16(vs + simm)); break;
                    case 0x22: case 0x26:
                        {
                            // lwl/lwr merge with the in-flight load of the same register.
                            uint cur = prevReg == (int)rt ? prevVal : vt;
                            uint a = vs + simm, al = a & ~3u, w = R32(al); int k = (int)(a & 3);
                            uint v = op == 0x22
                                ? (cur & (0x00FFFFFFu >> (k * 8))) | (w << ((3 - k) * 8))
                                : (cur & (0xFFFFFF00u << ((3 - k) * 8))) | (w >> (k * 8));
                            Load(rt, v);
                            if (prevReg == (int)rt) prevReg = 0;
                            break;
                        }
                    case 0x28: W8(vs + simm, vt); break;
                    case 0x29: W16(vs + simm, vt); break;
                    case 0x2B: W32(vs + simm, vt); break;
                    case 0x2A: case 0x2E:
                        {
                            uint a = vs + simm, al = a & ~3u, w = R32(al); int k = (int)(a & 3);
                            uint v = op == 0x2A
                                ? (w & (0xFFFFFF00u << (k * 8))) | (vt >> ((3 - k) * 8))
                                : (w & (0x00FFFFFFu >> ((3 - k) * 8))) | (vt << (k * 8));
                            W32(al, v);
                            break;
                        }
                    case 0x32: GWrite((int)rt, R32(vs + simm)); break;
                    case 0x3A: W32(vs + simm, GRead((int)rt)); break;
                    default: throw new Abort($"opcode {op:x2} ({ins:x8}) at {curPc:x8}");
                }

                // Retire the previous instruction's delayed load unless overwritten.
                if (prevReg != 0 && prevReg != wrote && prevReg != newLoadReg) r[prevReg] = prevVal;
                pendReg = newLoadReg; pendVal = newLoadVal;

                // The return jump's delay slot has executed once the next pc is the caller's
                // return address with the stack pointer balanced.
                if (pc == ret && r[29] == ctx.SP)
                {
                    if (pendReg != 0) { r[pendReg] = pendVal; pendReg = 0; }
                    res.Steps = steps;
                    break;
                }
            }
        }
        catch (Abort a)
        {
            res.Abort = a.Message;
        }
        res.V0 = r[2]; res.V1 = r[3];
        return res;
    }

    /// <summary>
    /// Independent minimal GTE: raw register file plus the OP command (cop2 0x0C), written
    /// from psx-spx. Anything else aborts the shadow run, so this variant only checks OP users.
    /// </summary>
    private sealed class MiniGte
    {
        private readonly uint[] _d = new uint[32];
        private readonly uint[] _c = new uint[32];

        public MiniGte()
        {
            for (int i = 0; i < 32; i++)
            {
                _d[i] = RecompOne.Runtime.Gte.Read(i);
                _c[i] = RecompOne.Runtime.Gte.ReadControl(i);
            }
        }

        private static int S16(uint v) => (short)(ushort)v;

        private static int _crossReports;

        // Feed the same operands to the runtime GTE and report the first disagreement.
        private void CrossCheck(uint cmd, long m1, long m2, long m3)
        {
            if (_crossReports >= 8) return;
            var G = typeof(RecompOne.Runtime.Gte);
            RecompOne.Runtime.Gte.WriteControl(0, _c[0]);
            RecompOne.Runtime.Gte.WriteControl(2, _c[2]);
            RecompOne.Runtime.Gte.WriteControl(4, _c[4]);
            RecompOne.Runtime.Gte.Write(9, _d[9]);
            RecompOne.Runtime.Gte.Write(10, _d[10]);
            RecompOne.Runtime.Gte.Write(11, _d[11]);
            RecompOne.Runtime.Gte.Execute(cmd);
            uint r1 = RecompOne.Runtime.Gte.Read(25), r2 = RecompOne.Runtime.Gte.Read(26), r3 = RecompOne.Runtime.Gte.Read(27);
            if (r1 == (uint)(int)m1 && r2 == (uint)(int)m2 && r3 == (uint)(int)m3) return;
            _crossReports++;
            Console.Error.WriteLine(
                $"[shadow] GTE OP DIFF cmd={cmd:x8} R11={S16(_c[0])} R22={S16(_c[2])} R33={S16(_c[4])} "
                + $"IR1={S16(_d[9])} IR2={S16(_d[10])} IR3={S16(_d[11])} | expected MAC=({(int)m1},{(int)m2},{(int)m3}) "
                + $"runtime MAC=({(int)r1},{(int)r2},{(int)r3}) runtimeRead(ctl0,2,4)=({RecompOne.Runtime.Gte.ReadControl(0):x8},{RecompOne.Runtime.Gte.ReadControl(2):x8},{RecompOne.Runtime.Gte.ReadControl(4):x8})");
        }

        public uint Read(int reg) => reg switch
        {
            9 or 10 or 11 => (uint)S16(_d[reg]),
            _ => _d[reg],
        };

        public void Write(int reg, uint v) => _d[reg] = v;
        public uint ReadControl(int reg) => _c[reg];
        public void WriteControl(int reg, uint v) => _c[reg] = v;

        public void Execute(uint cmd)
        {
            if ((cmd & 0x3F) != 0x0C) throw new Abort($"MiniGte: unsupported cmd {cmd:x8}");
            int sf = (cmd & (1u << 19)) != 0 ? 12 : 0;
            bool lm = (cmd & (1u << 10)) != 0;
            long d1 = S16(_c[0]), d2 = S16(_c[2]), d3 = S16(_c[4]);
            long ir1 = S16(_d[9]), ir2 = S16(_d[10]), ir3 = S16(_d[11]);
            long m1 = (ir3 * d2 - ir2 * d3) >> sf;
            long m2 = (ir1 * d3 - ir3 * d1) >> sf;
            long m3 = (ir2 * d1 - ir1 * d2) >> sf;
            _d[25] = (uint)(int)m1; _d[26] = (uint)(int)m2; _d[27] = (uint)(int)m3;
            CrossCheck(cmd, m1, m2, m3);
            long lo = lm ? 0 : -0x8000;
            _d[9] = (uint)(int)Math.Clamp(m1, lo, 0x7FFF);
            _d[10] = (uint)(int)Math.Clamp(m2, lo, 0x7FFF);
            _d[11] = (uint)(int)Math.Clamp(m3, lo, 0x7FFF);
        }
    }
}
