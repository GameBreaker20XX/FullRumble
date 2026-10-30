using System.Diagnostics;
using System.Numerics;
using ImGuiNET;

namespace NascarRumble.Host;

/// <summary>
/// Debug-mode overlay: host frame rate plus the race timing and the player's car state, read
/// straight from emulated RAM (addresses in notes/SESSION_011.md).
/// </summary>
public static class DebugHud
{
    private static readonly Stopwatch Clock = Stopwatch.StartNew();
    private static int _frames;
    private static double _fps;

    public static void Draw()
    {
        _frames++;
        if (Clock.Elapsed.TotalSeconds >= 0.5)
        {
            _fps = _frames / Clock.Elapsed.TotalSeconds;
            _frames = 0;
            Clock.Restart();
        }

        var lines = new List<string> { $"Game {HostOverlay.GameFps:0} fps   Host {_fps:0} fps   Race cap {NativeHooks.RaceFpsSetting}" };
        if (RecompOne.Runtime.Runtime.Mem is { } m && NativeHooks.InRace)
        {
            static bool Ram(uint a) => a is >= 0x80010000u and < 0x801FF000u;
            lines.Add($"Clock {m.ReadU32(0x800AF738u)}   delta {m.ReadU32(0x800AF6E0u)}");
            uint entity = m.ReadU32(0x800AF730u);
            uint control = Ram(entity) ? m.ReadU32(entity + 0xE0u) : 0u;
            uint vehicle = Ram(control) ? m.ReadU32(control) : 0u;
            if (Ram(vehicle))
            {
                int gear = unchecked((sbyte)m.ReadU8(vehicle + 0x3F4u));
                lines.Add($"{m.ReadU32(entity + 0xCCu)} mph   gear {(gear < 0 ? "N" : gear.ToString())}   rpm {m.ReadU32(vehicle + 0x3ECu) >> 8}");
                lines.Add($"Lap {m.ReadU32(vehicle + 0x328u) + 1}   segment {unchecked((int)m.ReadU32(vehicle + 0x324u))}   grip {(short)m.ReadU16(vehicle + 0x2B4u)}");
            }
        }

        var dl = ImGui.GetForegroundDrawList();
        var display = ImGui.GetIO().DisplaySize;
        float scale = MathF.Max(1f, display.Y / 720f);
        float size = 15f * scale, pad = 8f * scale, lineHeight = size + 2f * scale;
        float width = 300f * scale;
        var min = new Vector2(display.X - width - 12f * scale, (HostOverlay.ShowFps ? 70f : 34f) * scale);
        var max = min + new Vector2(width, pad * 2 + lines.Count * lineHeight);
        dl.AddRectFilled(min, max, 0xC0100808u, 5f * scale);
        dl.AddRect(min, max, 0xFFECBCC4u, 5f * scale, ImDrawFlags.None, 1.5f * scale);
        for (int i = 0; i < lines.Count; i++)
            dl.AddText(ImGui.GetFont(), size, min + new Vector2(pad, pad + i * lineHeight), 0xFF30E8F8u, lines[i]);
    }
}
