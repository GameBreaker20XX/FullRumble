using System.Numerics;
using System.Text.Json;
using ImGuiNET;
using RecompOne.Runtime.Hardware;
using Silk.NET.Input;

namespace NascarRumble.Host;

/// <summary>
/// Start screen shown after the disc is validated and before the game boots, styled after the
/// game's own menus (see docs/LAUNCHER.md). It sets display, graphics, frame rate and debug mode.
/// The background is the game's title art, decoded from the user's disc (CW/FEND/FELD.LSC).
/// </summary>
public static class Launcher
{
    private const string PrefsFile = "launcher.json";

    public sealed class Prefs
    {
        public int Width { get; set; } = 1280;
        public int Height { get; set; } = 720;
        public bool Fullscreen { get; set; }
        public int RenderScale { get; set; } = 4;
        public bool SmoothScaling { get; set; } = true;
        public int Fps { get; set; } = 30;
        public bool Debug { get; set; }
        public bool ShowFps { get; set; }
    }

    private enum Item { Start, DisplayMode, Resolution, Graphics, Scaling, Controller, FrameRate, ShowFps, Debug, Quit }

    private static readonly Item[] Items =
        [Item.Start, Item.DisplayMode, Item.Resolution, Item.Graphics, Item.Scaling, Item.Controller,
         Item.FrameRate, Item.ShowFps, Item.Debug, Item.Quit];
    private static readonly (int Width, int Height)[] Resolutions =
        [(960, 720), (1280, 720), (1600, 900), (1920, 1080), (2560, 1440)];

    private static Prefs _prefs = new();
    private static bool _prepared;
    private static int _selected;
    private static ushort _previousButtons = 0xFFFF;
    private static bool _done;
    private static bool _testingController;
    private static bool _previousMapKey, _previousBackKey;
    private static string _controllerMessage = "";
    private static nint _background;
    private static int _backgroundWidth, _backgroundHeight;
    private static ImFontPtr? _itemFont, _hintFont;

    /// <summary>Fonts must be requested before the window creates its ImGui context.</summary>
    public static void RequestFonts()
    {
        string[] candidates = OperatingSystem.IsMacOS()
            ? ["/System/Library/Fonts/Supplemental/Arial Narrow Bold.ttf"]
            : OperatingSystem.IsWindows()
                ? ["C:/Windows/Fonts/arialnb.ttf", "C:/Windows/Fonts/arialbd.ttf", "C:/Windows/Fonts/segoeuib.ttf"]
                : ["/usr/share/fonts/truetype/dejavu/DejaVuSans-Bold.ttf", "/usr/share/fonts/TTF/DejaVuSans-Bold.ttf"];
        string path = candidates.FirstOrDefault(File.Exists) ?? candidates[0];
        RecompOne.Runtime.Runtime.UiFontRequests.Add((path, 40f));
        RecompOne.Runtime.Runtime.UiFontRequests.Add((path, 26f));
    }

    /// <summary>
    /// Loads display preferences before Runtime.Initialize creates the window and GL backend.
    /// Other launcher choices are applied after the window exists, immediately before game boot.
    /// </summary>
    public static void PrepareStartupDisplay()
    {
        _prefs = Normalize(Load());
        _prepared = true;
        RecompOne.Runtime.Runtime.ConfigureStartupDisplay(
            _prefs.Width, _prefs.Height, _prefs.Fullscreen, _prefs.RenderScale, _prefs.SmoothScaling);
    }

    /// <summary>Loads saved choices and applies them without showing the screen.</summary>
    public static Prefs LoadAndApply()
    {
        if (!_prepared) PrepareStartupDisplay();
        Apply(_prefs);
        return _prefs;
    }

    /// <summary>Runtime.PreBoot callback: shows the screen until Start Game is chosen.</summary>
    public static void Run()
    {
        if (!_prepared) PrepareStartupDisplay();
        _done = false;
        _testingController = Environment.GetEnvironmentVariable("RUMBLE_CONTROLLER_TEST") == "1";
        _previousMapKey = _previousBackKey = false;
        if (RecompOne.Runtime.Runtime.UiFonts is [var item, var hint, ..])
        {
            _itemFont = item;
            _hintFont = hint;
        }
        LoadBackground();
        RecompOne.Runtime.Runtime.TopBarOverride = false;
        var previousOverlay = RecompOne.Runtime.Runtime.UiOverlay;
        RecompOne.Runtime.Runtime.UiOverlay = Draw;
        _previousButtons = Controller.State;
        while (!_done)
        {
            RecompOne.Runtime.Runtime.PumpUi();
            HandleInput();
            Thread.Sleep(10);
        }
        RecompOne.Runtime.Runtime.UiOverlay = previousOverlay;
        Save(_prefs);
        Apply(_prefs);
    }

    private static void Apply(Prefs prefs)
    {
        RecompOne.Runtime.Runtime.ApplyDisplaySettings(
            prefs.Width, prefs.Height, prefs.Fullscreen, prefs.RenderScale, prefs.SmoothScaling);
        NativeHooks.SetRaceFrameRate(prefs.Fps);
        RecompOne.Runtime.Runtime.TopBarOverride = prefs.Debug;
        NativeHooks.LapLogEnabled |= prefs.Debug;
        HostOverlay.ShowFps |= prefs.ShowFps;
        HostOverlay.ShowDebugHud = prefs.Debug;
        HostOverlay.Install();
        Console.Error.WriteLine(
            $"[launcher] display={prefs.Width}x{prefs.Height} fullscreen={(prefs.Fullscreen ? "on" : "off")} "
            + $"graphics={GraphicsName(prefs.RenderScale)} filter={(prefs.SmoothScaling ? "smooth" : "sharp")} "
            + $"race fps={prefs.Fps} show fps={(HostOverlay.ShowFps ? "on" : "off")} debug={(prefs.Debug ? "on" : "off")}");
    }

    private static void HandleInput()
    {
        ushort now = Controller.State;
        if (_testingController)
        {
            HandleControllerTest();
            _previousButtons = now;
            return;
        }

        bool Pressed(ushort bit) => (now & bit) == 0 && (_previousButtons & bit) != 0;
        if (Pressed(Controller.Up)) _selected = (_selected + Items.Length - 1) % Items.Length;
        if (Pressed(Controller.Down)) _selected = (_selected + 1) % Items.Length;
        int direction = Pressed(Controller.Left) ? -1 : Pressed(Controller.Right) ? 1 : 0;
        bool change = direction != 0;
        bool confirm = Pressed(Controller.Cross) || Pressed(Controller.Start);
        switch (Items[_selected])
        {
            case Item.DisplayMode when change || confirm:
                _prefs.Fullscreen = !_prefs.Fullscreen;
                break;
            case Item.Resolution when change || confirm:
                ChangeResolution(direction == 0 ? 1 : direction);
                break;
            case Item.Graphics when change || confirm:
                ChangeGraphics(direction == 0 ? 1 : direction);
                break;
            case Item.Scaling when change || confirm:
                _prefs.SmoothScaling = !_prefs.SmoothScaling;
                break;
            case Item.Controller when confirm:
                _testingController = true;
                _controllerMessage = "";
                _previousMapKey = RecompOne.Runtime.Runtime.IsKeyDown(Key.Enter);
                _previousBackKey = RecompOne.Runtime.Runtime.IsKeyDown(Key.Escape);
                break;
            case Item.FrameRate when change || confirm:
                _prefs.Fps = _prefs.Fps == 60 ? 30 : 60;
                break;
            case Item.ShowFps when change || confirm:
                _prefs.ShowFps = !_prefs.ShowFps;
                break;
            case Item.Debug when change || confirm:
                _prefs.Debug = !_prefs.Debug;
                break;
            case Item.Start when confirm:
                _done = true;
                break;
            case Item.Quit when confirm:
                Save(_prefs);
                RecompOne.Runtime.Runtime.Shutdown();
                Environment.Exit(0);
                break;
        }
        _previousButtons = now;
    }

    private static void HandleControllerTest()
    {
        bool mapKey = RecompOne.Runtime.Runtime.IsKeyDown(Key.Enter);
        bool backKey = RecompOne.Runtime.Runtime.IsKeyDown(Key.Escape);
        if (mapKey && !_previousMapKey)
        {
            _controllerMessage = RecompOne.Runtime.Runtime.ApplyStandardGamepadMapping()
                ? "Standard SDL mapping saved for Pad 1."
                : "Connect a compatible controller before mapping.";
        }
        else if (backKey && !_previousBackKey)
        {
            _testingController = false;
            _controllerMessage = "";
        }

        _previousMapKey = mapKey;
        _previousBackKey = backKey;
    }

    private static void ChangeResolution(int direction)
    {
        int index = Array.FindIndex(Resolutions,
            r => r.Width == _prefs.Width && r.Height == _prefs.Height);
        if (index < 0) index = 1;
        index = (index + direction + Resolutions.Length) % Resolutions.Length;
        (_prefs.Width, _prefs.Height) = Resolutions[index];
    }

    private static void ChangeGraphics(int direction)
    {
        int[] scales = [1, 2, 4];
        int index = Array.IndexOf(scales, _prefs.RenderScale);
        if (index < 0) index = 2;
        _prefs.RenderScale = scales[(index + direction + scales.Length) % scales.Length];
    }

    private static string GraphicsName(int scale) => scale switch
    {
        1 => "Original",
        2 => "Balanced",
        _ => "Enhanced",
    };

    private static void LoadBackground()
    {
        try
        {
            using var disc = RecompOne.Runtime.Cdrom.CueFs.Open(RecompOne.Runtime.Runtime.CdPath);
            var (rgba, width, height) = LscImage.Decode(disc.ReadFile("CW/FEND/FELD.LSC"));
            _background = RecompOne.Runtime.Runtime.CreateUiTexture(rgba, width, height);
            (_backgroundWidth, _backgroundHeight) = (width, height);
        }
        catch (Exception error)
        {
            Console.Error.WriteLine($"[launcher] title art unavailable: {error.Message}");
        }
    }

    // Palette sampled from the game's main menu.
    private static readonly uint Yellow = Rgba(248, 232, 48);
    private static readonly uint White = Rgba(240, 240, 240);
    private static readonly uint Lavender = Rgba(196, 188, 236);
    private static readonly uint BoxFill = Rgba(8, 8, 16, 190);
    private static readonly uint SelectedFill = Rgba(112, 12, 16, 235);
    private static readonly uint ValueFill = Rgba(36, 36, 40, 220);
    private static readonly uint Shadow = Rgba(0, 0, 0, 200);

    private static uint Rgba(int r, int g, int b, int a = 255) =>
        (uint)(r | (g << 8) | (b << 16) | (a << 24));

    private static void Draw()
    {
        var io = ImGui.GetIO();
        var display = io.DisplaySize;
        var dl = ImGui.GetForegroundDrawList();
        dl.AddRectFilled(Vector2.Zero, display, Rgba(0, 0, 0));

        // 4:3 screen, letterboxed, like the game's output.
        float height = MathF.Min(display.Y, display.X * 3f / 4f);
        float width = height * 4f / 3f;
        var origin = new Vector2((display.X - width) / 2f, (display.Y - height) / 2f);
        float unit = height / 240f;
        Vector2 P(float x, float y) => origin + new Vector2(x, y) * unit;

        if (_background != 0)
        {
            // The title art is 320x256; crop to the 240 visible lines, keeping the logo.
            float v1 = 240f / _backgroundHeight;
            dl.AddImage(_background, origin, origin + new Vector2(width, height), Vector2.Zero, new Vector2(1, v1));
        }
        dl.AddRectFilledMultiColor(P(0, 120), P(320, 240), Rgba(0, 0, 0, 0), Rgba(0, 0, 0, 0),
            Rgba(0, 0, 0, 170), Rgba(0, 0, 0, 170));

        var itemFont = _itemFont ?? ImGui.GetFont();
        var hintFont = _hintFont ?? ImGui.GetFont();
        float itemSize = 14f * unit, hintSize = 9.5f * unit;

        if (_testingController)
        {
            DrawControllerTest(dl, itemFont, hintFont, unit, origin);
            return;
        }

        // Hint box, top right, like "Select this for 1 player mode."
        string hint = Items[_selected] switch
        {
            Item.Start => "Start the game with these settings.",
            Item.DisplayMode => _prefs.Fullscreen
                ? "Use the whole display. F11 can toggle this during the game."
                : "Run in a resizable desktop window.",
            Item.Resolution => _prefs.Fullscreen
                ? "Window size used when leaving fullscreen."
                : "Output window size. This does not change game physics.",
            Item.Graphics => _prefs.RenderScale switch
            {
                1 => "Original PS1 internal resolution. Fastest and most authentic.",
                2 => "2x internal resolution. Balanced quality and GPU use.",
                _ => "4x internal resolution. Sharpest geometry; current default.",
            },
            Item.Scaling => _prefs.SmoothScaling
                ? "Smooth final image scaling."
                : "Sharp nearest-neighbor scaling.",
            Item.Controller => RecompOne.Runtime.Runtime.GetGamepadSnapshot() is { Connected: true } pad
                ? $"{pad.Name} detected. Press X to test its buttons."
                : "No SDL-compatible controller detected. You may connect one now.",
            Item.FrameRate => _prefs.Fps == 60
                ? "60 fps races. Experimental: launch grip differs slightly."
                : "30 fps races, the console's own cadence.",
            Item.ShowFps => _prefs.ShowFps
                ? "Frame counter in the top-right corner of the game."
                : "No frame counter on screen.",
            Item.Debug => _prefs.Debug
                ? "Shows the Debug menu bar, race HUD and lap log."
                : "Clean screen, no debug tools.",
            _ => "Close the game.",
        };
        DrawHintBox(dl, hintFont, hintSize, hint, P(180, 14), P(312, 54));

        // Menu box.
        const float top = 58f, rowH = 15.7f;
        var boxMin = P(76, top);
        var boxMax = P(236, top + Items.Length * rowH + 8f);
        dl.AddRectFilled(boxMin, boxMax, BoxFill, 6f * unit);
        dl.AddRect(boxMin, boxMax, Lavender, 6f * unit, ImDrawFlags.None, 1.6f * unit);

        for (int i = 0; i < Items.Length; i++)
        {
            var rowMin = P(80, top + 4f + i * rowH);
            var rowMax = P(232, top + 4f + i * rowH + rowH - 1f);
            bool selected = i == _selected;
            if (selected) dl.AddRectFilled(rowMin, rowMax, SelectedFill, 2f * unit);
            string label = Items[i] switch
            {
                Item.Start => "Start Game",
                Item.DisplayMode => "Display Mode",
                Item.Resolution => "Resolution",
                Item.Graphics => "Graphics",
                Item.Scaling => "Scaling",
                Item.Controller => "Controller Test",
                Item.FrameRate => "Frame Rate",
                Item.ShowFps => "Show FPS",
                Item.Debug => "Debug Mode",
                _ => "Quit",
            };
            var textPos = rowMin + new Vector2(5f * unit, 1.2f * unit);
            dl.AddText(itemFont, itemSize, textPos + new Vector2(1, 1) * unit, Shadow, label);
            dl.AddText(itemFont, itemSize, textPos, selected ? Yellow : White, label);

            string? value = Items[i] switch
            {
                Item.DisplayMode => _prefs.Fullscreen ? "Fullscreen" : "Windowed",
                Item.Resolution => $"{_prefs.Width}x{_prefs.Height}",
                Item.Graphics => GraphicsName(_prefs.RenderScale),
                Item.Scaling => _prefs.SmoothScaling ? "Smooth" : "Sharp",
                Item.Controller => RecompOne.Runtime.Runtime.GetGamepadSnapshot().Connected ? "Connected" : "Not Found",
                Item.FrameRate => _prefs.Fps == 60 ? "60 FPS" : "30 FPS",
                Item.ShowFps => _prefs.ShowFps ? "On" : "Off",
                Item.Debug => _prefs.Debug ? "On" : "Off",
                _ => null,
            };
            if (value == null) continue;
            DrawArrows(dl, P(200, top + 4f + i * rowH + rowH / 2f), unit, selected);
            var valueMin = P(244, top + 4f + i * rowH + 1f);
            var valueMax = P(312, top + 4f + i * rowH + rowH - 1.5f);
            dl.AddRectFilled(valueMin, valueMax, ValueFill, 1.5f * unit);
            dl.AddRect(valueMin, valueMax, Rgba(150, 150, 160), 1.5f * unit, ImDrawFlags.None, 1.2f * unit);
            dl.AddText(itemFont, itemSize * 0.82f, valueMin + new Vector2(4f * unit, 0.8f * unit), selected ? Yellow : White, value);
        }

        // Footer legend.
        string legend = "X Advance    Up/Down Change Selection    Left/Right Change Setting";
        var legendSize = hintFont.CalcTextSizeA(hintSize, float.MaxValue, 0f, legend);
        var legendPos = P(160, 224) - new Vector2(legendSize.X / 2f, 0);
        dl.AddText(hintFont, hintSize, legendPos + new Vector2(1, 1) * unit, Shadow, legend);
        dl.AddText(hintFont, hintSize, legendPos, Yellow, legend);

        dl.AddText(hintFont, hintSize * 0.8f, P(6, 4), Rgba(200, 200, 210, 200), "NASCAR Rumble Native - fan project");
    }

    private static void DrawControllerTest(
        ImDrawListPtr dl, ImFontPtr itemFont, ImFontPtr hintFont, float unit, Vector2 origin)
    {
        Vector2 P(float x, float y) => origin + new Vector2(x, y) * unit;
        var pad = RecompOne.Runtime.Runtime.GetGamepadSnapshot();
        float titleSize = 15f * unit, textSize = 9.5f * unit, chipSize = 8.5f * unit;

        dl.AddText(itemFont, titleSize, P(14, 14), Yellow, "CONTROLLER TEST");
        string status = pad.Connected ? $"CONNECTED: {pad.Name}" : "NO CONTROLLER DETECTED";
        dl.AddText(hintFont, textSize, P(14, 34), pad.Connected ? Rgba(120, 240, 140) : Rgba(255, 150, 80), status);
        dl.AddText(hintFont, textSize, P(14, 48), White,
            pad.Connected ? "Press buttons and move both sticks. Active inputs turn yellow."
                          : "Connect an SDL-compatible gamepad; hot-plug detection is enabled.");

        void Chip(string label, int input, float x, float y, float width)
        {
            bool active = pad.ActiveInputs.Contains(input);
            var min = P(x, y);
            var max = P(x + width, y + 15);
            dl.AddRectFilled(min, max, active ? SelectedFill : ValueFill, 2f * unit);
            dl.AddRect(min, max, active ? Yellow : Rgba(145, 145, 155), 2f * unit,
                ImDrawFlags.None, 1.1f * unit);
            var size = hintFont.CalcTextSizeA(chipSize, float.MaxValue, 0f, label);
            dl.AddText(hintFont, chipSize,
                new Vector2((min.X + max.X - size.X) / 2f, (min.Y + max.Y - size.Y) / 2f),
                active ? Yellow : White, label);
        }

        dl.AddText(hintFont, textSize, P(14, 70), Lavender, "D-PAD");
        Chip("UP", 11, 56, 66, 30); Chip("DOWN", 12, 89, 66, 38);
        Chip("LEFT", 13, 130, 66, 36); Chip("RIGHT", 14, 169, 66, 42);

        dl.AddText(hintFont, textSize, P(14, 91), Lavender, "FACE");
        Chip("A / X", 0, 56, 87, 36); Chip("B / O", 1, 95, 87, 36);
        Chip("X / SQ", 2, 134, 87, 40); Chip("Y / TR", 3, 177, 87, 40);

        dl.AddText(hintFont, textSize, P(14, 112), Lavender, "TOP");
        Chip("L1", 9, 56, 108, 27); Chip("R1", 10, 86, 108, 27);
        Chip("L2", 100, 116, 108, 27); Chip("R2", 101, 146, 108, 27);
        Chip("L3", 7, 176, 108, 27); Chip("R3", 8, 206, 108, 27);

        dl.AddText(hintFont, textSize, P(14, 133), Lavender, "SYSTEM");
        Chip("BACK", 4, 56, 129, 40); Chip("START", 6, 99, 129, 43);

        string sticks = $"LEFT STICK  {pad.LeftX,+5:0.00;-0.00}  {pad.LeftY,+5:0.00;-0.00}"
            + $"     RIGHT STICK  {pad.RightX,+5:0.00;-0.00}  {pad.RightY,+5:0.00;-0.00}";
        dl.AddText(hintFont, textSize, P(14, 153), White, sticks);
        string triggers = $"TRIGGERS    L2 {pad.LeftTrigger:0.00}    R2 {pad.RightTrigger:0.00}";
        dl.AddText(hintFont, textSize, P(14, 168), White, triggers);

        string active = pad.ActiveInputs.Length == 0
            ? "Detected input: none"
            : "Detected input: " + string.Join(", ", pad.ActiveInputs.Select(PadInputName));
        string message = _controllerMessage.Length > 0 ? _controllerMessage : active;
        DrawHintBox(dl, hintFont, textSize, message, P(14, 184), P(306, 211));

        string legend = "ENTER  Auto Map Pad 1        ESC  Back";
        var legendSize = hintFont.CalcTextSizeA(textSize, float.MaxValue, 0f, legend);
        dl.AddText(hintFont, textSize, P(160, 222) - new Vector2(legendSize.X / 2f, 0), Yellow, legend);
    }

    private static string PadInputName(int input) => input switch
    {
        0 => "A/Cross", 1 => "B/Circle", 2 => "X/Square", 3 => "Y/Triangle",
        4 => "Back", 5 => "Guide", 6 => "Start", 7 => "L3", 8 => "R3",
        9 => "L1", 10 => "R1", 11 => "D-Up", 12 => "D-Down", 13 => "D-Left", 14 => "D-Right",
        100 => "L2", 101 => "R2", 102 => "LStick Left", 103 => "LStick Right",
        104 => "LStick Up", 105 => "LStick Down", 106 => "RStick Left", 107 => "RStick Right",
        108 => "RStick Up", 109 => "RStick Down", _ => $"Button {input}",
    };

    private static void DrawArrows(ImDrawListPtr dl, Vector2 center, float unit, bool selected)
    {
        uint color = selected ? Yellow : White;
        float s = 3.5f * unit;
        var l = center + new Vector2(-6f * unit, 0);
        var r = center + new Vector2(6f * unit, 0);
        dl.AddTriangleFilled(l + new Vector2(-s, 0), l + new Vector2(s * 0.6f, -s), l + new Vector2(s * 0.6f, s), color);
        dl.AddTriangleFilled(r + new Vector2(s, 0), r + new Vector2(-s * 0.6f, -s), r + new Vector2(-s * 0.6f, s), color);
    }

    private static void DrawHintBox(ImDrawListPtr dl, ImFontPtr font, float size, string text, Vector2 min, Vector2 max)
    {
        float unit = (max.Y - min.Y) / 36f;
        dl.AddRectFilled(min, max, Rgba(24, 24, 28, 220), 4f * unit);
        dl.AddRect(min, max, Lavender, 4f * unit, ImDrawFlags.None, 1.4f * unit);
        float wrap = max.X - min.X - 8f * unit;
        var textSize = font.CalcTextSizeA(size, float.MaxValue, wrap, text);
        var pos = new Vector2((min.X + max.X - textSize.X) / 2f, (min.Y + max.Y - textSize.Y) / 2f);
        dl.AddText(font, size, pos, Yellow, text, wrap);
    }

    private static Prefs Load()
    {
        try
        {
            if (File.Exists(PrefsFile))
                return JsonSerializer.Deserialize<Prefs>(File.ReadAllText(PrefsFile)) ?? new Prefs();
        }
        catch (Exception error)
        {
            Console.Error.WriteLine($"[launcher] ignoring {PrefsFile}: {error.Message}");
        }
        return new Prefs();
    }

    private static Prefs Normalize(Prefs prefs)
    {
        if (!Resolutions.Any(r => r.Width == prefs.Width && r.Height == prefs.Height))
            (prefs.Width, prefs.Height) = (1280, 720);
        if (prefs.RenderScale is not (1 or 2 or 4)) prefs.RenderScale = 4;
        if (prefs.Fps is not (30 or 60)) prefs.Fps = 30;
        return prefs;
    }

    private static void Save(Prefs prefs)
    {
        try
        {
            File.WriteAllText(PrefsFile, JsonSerializer.Serialize(prefs, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch (Exception error)
        {
            Console.Error.WriteLine($"[launcher] could not save {PrefsFile}: {error.Message}");
        }
    }
}
