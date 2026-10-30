using System.Diagnostics;
using System.Numerics;
using ImGuiNET;

namespace NascarRumble.Host;

/// <summary>
/// In-game overlays drawn by the host over the game image: the frame-rate counter (launcher
/// option "Show FPS" or RUMBLE_SHOW_FPS=1) and, in debug mode, the race HUD.
/// </summary>
public static class HostOverlay
{
    public static bool ShowFps { get; set; } = Environment.GetEnvironmentVariable("RUMBLE_SHOW_FPS") == "1";
    public static bool ShowDebugHud { get; set; }

    private static readonly Stopwatch Window = Stopwatch.StartNew();
    private static int _gameFrames;
    private static double _gameFps;

    /// <summary>Called once per game frame: each race-loop iteration, or each frontend swap.</summary>
    public static void CountGameFrame()
    {
        _gameFrames++;
        double seconds = Window.Elapsed.TotalSeconds;
        if (seconds < 0.5) return;
        _gameFps = _gameFrames / seconds;
        _gameFrames = 0;
        Window.Restart();
    }

    /// <summary>Game frames per second over the last half second.</summary>
    public static double GameFps => Window.Elapsed.TotalSeconds > 1.5 ? 0 : _gameFps;

    public static void Install()
    {
        if (ShowFps || ShowDebugHud) RecompOne.Runtime.Runtime.UiOverlay = Draw;
    }

    private static void Draw()
    {
        if (ShowFps) DrawFps();
        if (ShowDebugHud) DebugHud.Draw();
    }

    /// <summary>Top-right corner of the 4:3 game image, in the style of the game's hint boxes.</summary>
    private static void DrawFps()
    {
        var display = ImGui.GetIO().DisplaySize;
        float top = RecompOne.Runtime.Runtime.TopBarOverride == true ? ImGui.GetFrameHeight() : 0f;
        float height = MathF.Min(display.Y - top, display.X * 3f / 4f);
        float width = height * 4f / 3f;
        float unit = height / 240f;
        var corner = new Vector2((display.X + width) / 2f, (display.Y - top - height) / 2f + top);

        double fps = GameFps;
        string text = fps > 0 ? $"{fps:0} FPS" : "-- FPS";
        float size = 9f * unit, pad = 3f * unit;
        var font = ImGui.GetFont();
        var textSize = font.CalcTextSizeA(size, float.MaxValue, 0f, "00 FPS");
        var max = corner + new Vector2(-4f * unit, 4f * unit + textSize.Y + pad * 2);
        var min = new Vector2(max.X - textSize.X - pad * 2 - 6f * unit, corner.Y + 4f * unit);

        uint color = fps >= 50 ? 0xFF60E070u : fps >= 25 ? 0xFF30E8F8u : 0xFF4050F0u; // green/yellow/red (ABGR)
        var dl = ImGui.GetForegroundDrawList();
        dl.AddRectFilled(min, max, 0xC8100808u, 3f * unit);
        dl.AddRect(min, max, 0xFFECBCC4u, 3f * unit, ImDrawFlags.None, 1.2f * unit);
        dl.AddCircleFilled(new Vector2(min.X + pad + 1.5f * unit, (min.Y + max.Y) / 2f), 1.6f * unit, color);
        var pos = new Vector2(min.X + pad + 5f * unit, min.Y + pad);
        dl.AddText(font, size, pos + new Vector2(0.8f, 0.8f) * unit, 0xC8000000u, text);
        dl.AddText(font, size, pos, color, text);
    }
}
