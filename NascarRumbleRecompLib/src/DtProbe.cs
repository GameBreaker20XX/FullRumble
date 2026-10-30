using System.Reflection;
using RecompOne.Runtime.Context;
using RecompOne.Runtime.Memory;

namespace NascarRumble.Host;

/// <summary>
/// Step-size probe for the 60 fps work. With RUMBLE_DT_PROBE=1, every 20th race frame the vehicle
/// step FUN_80026d3c(dt) is run on a snapshot of the machine once with dt=10 (console cadence)
/// and twice with dt=5 (60 fps), then the state is restored and the real frame proceeds. Fields
/// of the first vehicle that end up different are logged, so per-frame (not per-dt) logic shows
/// up directly.
/// </summary>
public static class DtProbe
{
    private static readonly bool Enabled = Environment.GetEnvironmentVariable("RUMBLE_DT_PROBE") == "1";
    private static readonly FieldInfo RamField =
        typeof(PSMemory).GetField("_ram", BindingFlags.Instance | BindingFlags.NonPublic)!;
    private static readonly FieldInfo ScratchField =
        typeof(PSMemory).GetField("_scratchpad", BindingFlags.Instance | BindingFlags.NonPublic)!;

    [ThreadStatic] private static bool _running;
    [ThreadStatic] private static uint _frame;

    private static readonly int Substeps =
        int.TryParse(Environment.GetEnvironmentVariable("RUMBLE_VEH_SUBSTEPS"), out int n) && n > 1 ? n : 1;

    public static void VehicleStepPre(CpuContext c, IMemory m)
    {
        if (Substeps > 1 && !_running)
        {
            // Experiment: split the vehicle step into equal sub-steps; the last one is the real call.
            uint dt = c.A0 / (uint)Substeps;
            var saved = c.Snapshot();
            _running = true;
            for (int i = 1; i < Substeps; i++)
            {
                Step(c, m, dt);
                c.Restore(saved);
            }
            _running = false;
            c.A0 = dt;
            return;
        }
        if (!Enabled || _running || m is not PSMemory psm) return;
        if (++_frame % 20u != 0u) return;
        uint vehicle = m.ReadU32(0x800B12C0u);
        if (vehicle is < 0x80010000u or >= 0x801FF000u) return;
        uint entity = m.ReadU32(vehicle);

        var ram = (byte[])RamField.GetValue(psm)!;
        var scratch = (byte[])ScratchField.GetValue(psm)!;
        byte[] ramSnap = (byte[])ram.Clone(), scratchSnap = (byte[])scratch.Clone();
        var regs = c.Snapshot();
        object?[] gte = ShadowCpu.SnapshotGte();
        byte[] before = Capture(m, vehicle, entity);

        _running = true;
        try
        {
            Step(c, m, 10u);
            byte[] coarse = Capture(m, vehicle, entity);
            Restore();
            Step(c, m, 5u);
            c.Restore(regs);
            Step(c, m, 5u);
            byte[] fine = Capture(m, vehicle, entity);
            Restore();
            Report(m, before, coarse, fine);
        }
        finally
        {
            _running = false;
        }

        void Restore()
        {
            ramSnap.CopyTo(ram, 0);
            scratchSnap.CopyTo(scratch, 0);
            c.Restore(regs);
            ShadowCpu.RestoreGte(gte);
        }
    }

    private static readonly uint SubstepFunction =
        uint.TryParse(Environment.GetEnvironmentVariable("RUMBLE_SUBSTEP_FN"),
            System.Globalization.NumberStyles.HexNumber, null, out uint fn) ? fn : 0u;

    public static bool Running => _running;

    /// <summary>
    /// Experiment: RUMBLE_SUBSTEP_FN=80056c6c runs that function (dt in A0) as two half steps
    /// while the rest of the frame keeps the full dt.
    /// </summary>
    public static void SubstepPre(uint function, CpuContext c, IMemory m, Action<CpuContext, IMemory> call)
    {
        if (SubstepFunction != function || _running) return;
        uint dt = c.A0 / 2u;
        var saved = c.Snapshot();
        _running = true;
        c.A0 = dt;
        call(c, m);
        c.Restore(saved);
        _running = false;
        c.A0 = dt;
    }

    private static void Step(CpuContext c, IMemory m, uint dt)
    {
        c.A0 = dt;
        Recompiled.NASCAR_Rumble__USA_.FUN_80026d3c(c, m);
    }

    private static byte[] Capture(IMemory m, uint vehicle, uint entity)
    {
        var bytes = new byte[0x600];
        for (uint i = 0; i < 0x500; i += 4) BitConverter.TryWriteBytes(bytes.AsSpan((int)i), m.ReadU32(vehicle + i));
        for (uint i = 0; i < 0x100; i += 4) BitConverter.TryWriteBytes(bytes.AsSpan(0x500 + (int)i), m.ReadU32(entity + i));
        return bytes;
    }

    private static void Report(IMemory m, byte[] before, byte[] coarse, byte[] fine)
    {
        var lines = new List<string>();
        for (int i = 0; i < 0x600; i += 4)
        {
            int b = BitConverter.ToInt32(before, i), x = BitConverter.ToInt32(coarse, i), y = BitConverter.ToInt32(fine, i);
            if (x == y) continue;
            string where = i < 0x500 ? $"veh+{i:x3}" : $"ent+{i - 0x500:x3}";
            lines.Add($"{where}: before={b} 1x10={x} 2x5={y}");
        }
        uint raw = m.ReadU32(0x800AF738u);
        Console.Error.WriteLine($"[dtprobe] raw={raw} differing words={lines.Count}");
        foreach (string line in lines) Console.Error.WriteLine("[dtprobe]   " + line);
    }
}
