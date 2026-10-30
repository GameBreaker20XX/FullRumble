using RecompOne.Runtime.Context;
using RecompOne.Runtime.Memory;

namespace NascarRumble.Host;

/// <summary>
/// RUMBLE_CARD_TRACE=1: logs the game's memory-card driver (state block at *(0x800AA598)) on
/// every card BIOS call and event callback, to compare the event sequence with the console.
/// </summary>
public static class CardTrace
{
    private static readonly bool Enabled = Environment.GetEnvironmentVariable("RUMBLE_CARD_TRACE") == "1";

    private const uint DriverPointer = 0x800AA598u;

    private static void Log(IMemory m, string what)
    {
        uint d = m.ReadU32(DriverPointer);
        if (d is < 0x80010000u or >= 0x80200000u)
        {
            Console.Error.WriteLine($"[card] {what} (no driver)");
            return;
        }
        Console.Error.WriteLine(
            $"[card] {what} state={m.ReadU32(d + 0x8F4u)} req={m.ReadU32(d + 0x8F8u)} "
            + $"status={unchecked((int)m.ReadU32(d + 0x900u))} loaded={m.ReadU32(d + 0x904u)} "
            + $"wantLoad={m.ReadU32(d + 0x908u)} skip={m.ReadU32(d + 0x994u)} "
            + $"frame={m.ReadU32(d + 0x998u)} left={unchecked((int)m.ReadU32(d + 0x99Cu))} "
            + $"retry={unchecked((int)m.ReadU32(d + 0x9A0u))} err={m.ReadU32(d + 0x9A4u)} "
            + $"watch={m.ReadU32(d + 0x1BB0u)}");
    }

    public static void CardInfo(CpuContext c, IMemory m) { if (Enabled) Log(m, $"_card_info({c.A0:x})"); }
    public static void CardLoad(CpuContext c, IMemory m) { if (Enabled) Log(m, $"_card_load({c.A0:x})"); }
    public static void CardRead(CpuContext c, IMemory m) { if (Enabled) Log(m, $"_card_read({c.A0:x},{c.A1})"); }
    public static void CardWrite(CpuContext c, IMemory m) { if (Enabled) Log(m, $"_card_write({c.A0:x},{c.A1})"); }
    public static void CardClear(CpuContext c, IMemory m) { if (Enabled) Log(m, $"_card_clear({c.A0:x})"); }
    public static void HwIoe(CpuContext c, IMemory m) { if (Enabled) Log(m, "cb Hw IOE"); }
    public static void HwNew(CpuContext c, IMemory m) { if (Enabled) Log(m, "cb Hw NEWCARD"); }
    public static void HwTimeout(CpuContext c, IMemory m) { if (Enabled) Log(m, "cb Hw TIMEOUT"); }
    public static void HwError(CpuContext c, IMemory m) { if (Enabled) Log(m, "cb Hw ERROR"); }
    public static void SwIoe(CpuContext c, IMemory m) { if (Enabled) Log(m, "cb Sw IOE"); }
    public static void SwNew(CpuContext c, IMemory m) { if (Enabled) Log(m, "cb Sw NEWCARD"); }
    public static void SwTimeout(CpuContext c, IMemory m) { if (Enabled) Log(m, "cb Sw TIMEOUT"); }
    public static void SwError(CpuContext c, IMemory m) { if (Enabled) Log(m, "cb Sw ERROR"); }
    public static void Watchdog(CpuContext c, IMemory m)
    {
        if (!Enabled) return;
        uint d = m.ReadU32(DriverPointer);
        if (d is >= 0x80010000u and < 0x80200000u && m.ReadU32(d + 0x1BB0u) > 0x3Cu) Log(m, "vsync watchdog fires");
    }

    // Request entry points used by the frontend.
    public static void RequestRead(CpuContext c, IMemory m) { if (Enabled) Log(m, $"request read frame={c.A0} n={c.A1}"); }
    public static void RequestWrite(CpuContext c, IMemory m) { if (Enabled) Log(m, $"request write frame={c.A0} n={c.A1}"); }
    public static void RequestCreate(CpuContext c, IMemory m) { if (Enabled) Log(m, $"request create frame={c.A0} n={c.A1}"); }
    public static void RequestFormat(CpuContext c, IMemory m) { if (Enabled) Log(m, "request format"); }
    public static void Open(CpuContext c, IMemory m) { if (Enabled) Log(m, "driver open"); }
    public static void Close(CpuContext c, IMemory m) { if (Enabled) Log(m, "driver close"); }
}
