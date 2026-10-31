using System.Runtime.InteropServices;
using System.Text;
using FullRumble.Config;
using FullRumble.Disc;

namespace FullRumble;

/// <summary>
/// First-run "add your disc" screen. Deliberately a plain Win32 window rather than the in-game ImGui
/// overlay: first run happens before any graphics context exists, and a native window is composited
/// correctly by the desktop on every driver, so the screen can never come up black or washed out.
/// </summary>
internal static class SetupUi
{
    private const int IdTitle = 101;
    private const int IdLegal = 102;
    private const int IdStatus = 103;
    private const int IdMessage = 104;
    private const int IdAddFile = 105;
    private const int IdAddFolder = 106;
    private const int IdChooseLabel = 107;
    private const int IdCandidates = 108;
    private const int IdShowLog = 109;
    private const int IdCopyLog = 110;
    private const int IdLog = 111;
    private const int IdSkip = 112;
    private const int IdHint = 113;
    private const int IdVersion = 114;

    private const uint WS_CHILD = 0x40000000;
    private const uint WS_VISIBLE = 0x10000000;
    private const uint WS_TABSTOP = 0x00010000;
    private const uint WS_BORDER = 0x00800000;
    private const uint WS_VSCROLL = 0x00200000;
    private const uint SS_CENTER = 0x00000001;
    private const uint BS_OWNERDRAW = 0x0000000B;
    private const uint BS_AUTOCHECKBOX = 0x00000003;
    private const uint ES_MULTILINE = 0x00000004;
    private const uint ES_READONLY = 0x00000800;
    private const uint ES_AUTOVSCROLL = 0x00000040;
    private const uint LBS_NOTIFY = 0x00000001;
    private const uint WS_EX_CONTROLPARENT = 0x00010000;
    private const int GWL_ID = -12;

    private const uint WM_CREATE = 0x0001;
    private const uint WM_DESTROY = 0x0002;
    private const uint WM_CLOSE = 0x0010;
    private const uint WM_COMMAND = 0x0111;
    private const uint WM_DRAWITEM = 0x002B;
    private const uint WM_CTLCOLORBTN = 0x0135;
    private const uint WM_CTLCOLOREDIT = 0x0133;
    private const uint WM_CTLCOLORSTATIC = 0x0138;
    private const uint WM_DPICHANGED = 0x02E0;

    private const uint LB_ADDSTRING = 0x0180;
    private const uint LB_RESETCONTENT = 0x0184;
    private const uint LB_GETCURSEL = 0x0188;
    private const uint BM_GETCHECK = 0x00F0;
    private const uint BM_SETCHECK = 0x00F1;
    private const uint WM_SETFONT = 0x0030;

    private static DiscStatus _status = DiscStatus.NotAdded;
    private static string _message = "No disc added yet. Add the dump you made from your own copy of the game.";
    private static readonly List<string> Log = new();
    private static IReadOnlyList<string> _candidates = Array.Empty<string>();
    private static bool _showLog = true;
    private static string? _result;

    private static nint _hwnd;
    private static nint _font;
    private static nint _fontBig;
    private static nint _bgBrush;
    private static nint _logBrush;
    private static float _scale = 1f;
    private static WndProcDelegate? _proc;

    private delegate nint WndProcDelegate(nint hwnd, uint msg, nint wParam, nint lParam);

    public static string? Run(string? previousPath, DiscValidationResult? previous)
    {
        if (previous is not null)
        {
            _status = previous.Status == DiscStatus.Valid ? DiscStatus.NotAdded : previous.Status;
            _message = previous.Message;
            Log.AddRange(previous.Checks);
            _candidates = previous.Candidates;
            if (previousPath is not null) Log.Add($"previous choice: {previousPath}");
        }

        SetProcessDpiAwarenessContext(-4); // per-monitor v2

        _proc = WndProc;
        var wc = new WndClassEx
        {
            Size = Marshal.SizeOf<WndClassEx>(),
            Style = 0x0003,
            WndProc = Marshal.GetFunctionPointerForDelegate(_proc),
            ClassName = "FullRumbleSetup",
            Instance = GetModuleHandleW(0),
            Icon = LoadIconW(0, new nint(0x7F00)),
        };
        if (RegisterClassExW(ref wc) == 0) return null;

        _hwnd = CreateWindowExW(WS_EX_CONTROLPARENT, "FullRumbleSetup", "FullRumble Setup",
            0x00C00000 | 0x00080000 | 0x00020000,
            0, 0, 760, 620, 0, 0, 0, 0);
        if (_hwnd == 0) return null;

        ShowWindow(_hwnd, 5);
        UpdateWindow(_hwnd);

        while (GetMessageW(out var msg, 0, 0, 0) > 0)
        {
            TranslateMessage(ref msg);
            DispatchMessageW(ref msg);
        }

        DeleteObject(_font);
        DeleteObject(_fontBig);
        DeleteObject(_bgBrush);
        DeleteObject(_logBrush);
        return _result;
    }

    private static nint WndProc(nint hwnd, uint msg, nint wParam, nint lParam)
    {
        switch (msg)
        {
            case WM_CREATE:
                OnCreate(hwnd);
                return 0;
            case WM_CLOSE:
                DestroyWindow(hwnd);
                return 0;
            case 0x0014: // WM_ERASEBKGND
                GetClientRect(hwnd, out var erc);
                FillRect(wParam, ref erc, _bgBrush);
                return 1;
            case WM_DESTROY:
                PostQuitMessage(0);
                return 0;
            case WM_DPICHANGED:
            {
                var rect = Marshal.PtrToStructure<Rect>(lParam)!;
                SetWindowPos(hwnd, 0, rect.Left, rect.Top, rect.Right - rect.Left, rect.Bottom - rect.Top, 0x0040);
                RefreshFonts();
                Layout();
                return 0;
            }
            case WM_COMMAND:
                OnCommand(hwnd, wParam);
                return 0;
            case 0x0233: // WM_DROPFILES
            {
                int count = DragQueryFileW(wParam, 0xFFFFFFFF, null, 0);
                if (count > 0)
                {
                    int len = DragQueryFileW(wParam, 0, null, 0) + 1;
                    var buffer = new char[len];
                    if (DragQueryFileW(wParam, 0, buffer, len) > 0)
                        Check(new string(buffer, 0, len - 1));
                }
                DragFinish(wParam);
                return 0;
            }
            case WM_DRAWITEM:
                return DrawItem(lParam) ? 1 : 0;
            case WM_CTLCOLOREDIT:
                SetBkMode(wParam, 1);
                SetTextColor(wParam, Rgb(242, 244, 247));
                SetBkColor(wParam, Rgb(22, 24, 29));
                return _logBrush;
            case WM_CTLCOLORSTATIC:
            {
                int id = GetWindowLongPtrW(lParam, GWL_ID);
                SetBkMode(wParam, 1);
                SetTextColor(wParam, id == IdStatus ? StatusColor() : id == IdVersion ? Rgb(154, 163, 174) : Rgb(242, 244, 247));
                return _bgBrush;
            }
            case WM_CTLCOLORBTN:
                SetBkMode(wParam, 1);
                SetTextColor(wParam, Rgb(242, 244, 247));
                return _bgBrush;
        }
        return DefWindowProcW(hwnd, msg, wParam, lParam);
    }

    private static void OnCreate(nint hwnd)
    {
        _hwnd = hwnd;
        _scale = GetDpiForWindow(hwnd) / 96f;
        RefreshFonts();
        _bgBrush = CreateSolidBrush(Rgb(18, 20, 24));
        _logBrush = CreateSolidBrush(Rgb(22, 24, 29));

        int screenW = GetSystemMetrics(0);
        int screenH = GetSystemMetrics(1);
        int width = S(760);
        int height = S(638);
        SetWindowPos(hwnd, 0, (screenW - width) / 2, (screenH - height) / 2, width, height, 0x0040);

        Static(IdTitle, "Add your disc to play", SS_CENTER, _fontBig);
        Static(IdLegal, "FullRumble plays NASCAR Rumble (USA, Canada, SLUS-01068) from a dump you make from a disc you own. " +
            "This project does not include any game assets. See docs/DUMPING.md for how to make one. " +
            "An accepted disc is copied into the game's own disc folder, so this is a one-time step and your dump stays where it is.", 0, _font);
        Static(IdStatus, StatusText(), 0, _font);
        Static(IdMessage, _message, 0, _font);
        Button(IdAddFile, "Add disc file...", BS_OWNERDRAW | WS_TABSTOP);
        Button(IdAddFolder, "Add folder...", BS_OWNERDRAW | WS_TABSTOP);
        Static(IdChooseLabel, "Several disc images were found. Choose the one that is your dump:", 0, _font);
        CreateWindowExW(0, "ListBox", "", WS_CHILD | WS_BORDER | WS_VSCROLL | WS_TABSTOP | LBS_NOTIFY,
            0, 0, 0, 0, hwnd, new nint(IdCandidates), 0, 0);
        SendMessageW(GetDlgItem(hwnd, IdCandidates), WM_SETFONT, _font, 1);
        Button(IdShowLog, "Show progress log", BS_AUTOCHECKBOX);
        Button(IdCopyLog, "Copy log", BS_OWNERDRAW | WS_TABSTOP);
        CreateWindowExW(0, "Edit", string.Join(Environment.NewLine, Log),
            WS_CHILD | WS_BORDER | WS_VSCROLL | ES_MULTILINE | ES_READONLY | ES_AUTOVSCROLL,
            0, 0, 0, 0, hwnd, new nint(IdLog), 0, 0);
        SendMessageW(GetDlgItem(hwnd, IdLog), WM_SETFONT, _font, 1);
        Button(IdSkip, "Skip for now", BS_OWNERDRAW | WS_TABSTOP);
        Static(IdHint, "You can add the disc later from this screen, or with:  FullRumble --set-disc PATH", 0, _font);
        Static(IdVersion, $"FullRumble {Version}", 0x00000002, _font);

        SendMessageW(GetDlgItem(hwnd, IdShowLog), BM_SETCHECK, 1, 0);
        DragAcceptFiles(hwnd, true);
        Layout();
    }

    private static void RefreshFonts()
    {
        int dpi = (int)(96 * _scale);
        if (_font != 0) DeleteObject(_font);
        if (_fontBig != 0) DeleteObject(_fontBig);
        _font = CreateFontW(-MulDiv(11, dpi, 72), 0, 0, 0, 400, 0, 0, 0, 0, 0, 0, 5, 0, "Segoe UI");
        _fontBig = CreateFontW(-MulDiv(19, dpi, 72), 0, 0, 0, 600, 0, 0, 0, 0, 0, 0, 5, 0, "Segoe UI");
    }

    private static void Static(int id, string text, uint extra, nint font)
    {
        nint h = CreateWindowExW(0, "STATIC", text, WS_CHILD | WS_VISIBLE | extra, 0, 0, 0, 0, _hwnd, new nint(id), 0, 0);
        SendMessageW(h, WM_SETFONT, font, 1);
    }

    private static void Button(int id, string text, uint style)
    {
        nint h = CreateWindowExW(0, "BUTTON", text, WS_CHILD | WS_VISIBLE | style, 0, 0, 0, 0, _hwnd, new nint(id), 0, 0);
        SendMessageW(h, WM_SETFONT, _font, 1);
    }

    private static int S(int v) => (int)(v * _scale);

    private static void Layout()
    {
        GetClientRect(_hwnd, out var rc);
        int pad = S(28);
        int w = rc.Right - pad * 2;
        int y = pad;

        Place(IdTitle, pad, y, w, S(30)); y += S(34);
        Place(IdLegal, pad, y, w, S(64)); y += S(70);
        Place(IdStatus, pad, y, w, S(20)); y += S(24);
        Place(IdMessage, pad, y, w, S(40)); y += S(46);
        Place(IdAddFile, pad, y, S(200), S(38));
        Place(IdAddFolder, pad + S(216), y, S(200), S(38)); y += S(52);

        bool hasCandidates = _candidates.Count > 0;
        Show(IdChooseLabel, hasCandidates);
        Show(IdCandidates, hasCandidates);
        if (hasCandidates)
        {
            Place(IdChooseLabel, pad, y, w, S(18)); y += S(22);
            Place(IdCandidates, pad, y, w, S(96)); y += S(104);
        }

        Place(IdShowLog, pad, y, S(160), S(22));
        Place(IdCopyLog, pad + S(168), y - S(3), S(110), S(26)); y += S(28);

        int logBottom = rc.Bottom - S(104);
        Show(IdLog, _showLog);
        if (_showLog) Place(IdLog, pad, y, w, Math.Max(S(60), logBottom - y));

        Place(IdSkip, pad, rc.Bottom - S(58), S(150), S(34));
        Place(IdHint, pad, rc.Bottom - S(86), w, S(20));
        Place(IdVersion, rc.Right - pad - S(160), rc.Bottom - S(50), S(160), S(20));
    }

    private static void Place(int id, int x, int y, int w, int h) =>
        SetWindowPos(GetDlgItem(_hwnd, id), 0, x, y, w, h, 0x0040 | 0x0004);

    private static void Show(int id, bool visible) =>
        ShowWindow(GetDlgItem(_hwnd, id), visible ? 5 : 0);

    private static void OnCommand(nint hwnd, nint wParam)
    {
        long wp = wParam.ToInt64();
        int notify = (int)((wp >> 16) & 0xFFFF);
        int id = (int)(wp & 0xFFFF);

        if (id == IdCandidates && notify == 1) // LBN_SELCHANGE
        {
            int sel = (int)SendMessageW(GetDlgItem(hwnd, IdCandidates), LB_GETCURSEL, 0, 0).ToInt64();
            if (sel >= 0 && sel < _candidates.Count) Check(_candidates[sel]);
            return;
        }
        if (id == IdShowLog)
        {
            _showLog = SendMessageW(GetDlgItem(hwnd, IdShowLog), BM_GETCHECK, 0, 0).ToInt64() == 1;
            Layout();
            return;
        }

        switch (id)
        {
            case IdAddFile: Check(OpenFile(hwnd)); break;
            case IdAddFolder: Check(OpenFolder(hwnd)); break;
            case IdCopyLog: CopyToClipboard(string.Join(Environment.NewLine, Log)); break;
            case IdSkip: _result = null; DestroyWindow(hwnd); break;
        }
    }

    private static bool DrawItem(nint lParam)
    {
        var dis = Marshal.PtrToStructure<DrawItemStruct>(lParam)!;
        if (dis.CtlID is not (IdAddFile or IdAddFolder or IdCopyLog or IdSkip)) return false;

        bool focused = (dis.ItemState & 0x0010) != 0;
        nint brush = CreateSolidBrush(focused ? Rgb(74, 134, 236) : Rgb(46, 111, 216));
        var box = new Rect { Left = dis.RcLeft, Top = dis.RcTop, Right = dis.RcRight, Bottom = dis.RcBottom };
        FillRect(dis.Hdc, ref box, brush);
        DeleteObject(brush);

        var text = new StringBuilder(64);
        GetWindowTextW(dis.HwndItem, text, 64);
        SetBkMode(dis.Hdc, 1);
        SetTextColor(dis.Hdc, Rgb(255, 255, 255));
        nint old = SelectObject(dis.Hdc, _font);
        DrawTextW(dis.Hdc, text, -1, ref box, 0x00000001 | 0x00000004 | 0x00000020);
        SelectObject(dis.Hdc, old);

        if (focused)
        {
            var inner = new Rect { Left = box.Left + 2, Top = box.Top + 2, Right = box.Right - 2, Bottom = box.Bottom - 2 };
            DrawFocusRect(dis.Hdc, ref inner);
        }
        return true;
    }

    private static void Check(string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return;
        _status = DiscStatus.Checking;
        _message = "Checking...";
        SetWindowTextW(GetDlgItem(_hwnd, IdStatus), StatusText());
        SetWindowTextW(GetDlgItem(_hwnd, IdMessage), _message);
        UpdateWindow(_hwnd);

        Log.Add($"checking {path}");
        DiscValidationResult result;
        try
        {
            result = DiscValidator.Validate(path);
        }
        catch (Exception error)
        {
            result = new DiscValidationResult(DiscStatus.Invalid, $"Unexpected error: {error.Message}",
                Array.Empty<string>(), Array.Empty<string>(), null, null);
        }

        _status = result.Status;
        _message = result.Message;
        _candidates = result.Candidates;
        Log.AddRange(result.Checks);

        if (result.Status == DiscStatus.Valid)
        {
            _message = "Copying the disc into the game folder...";
            SetWindowTextW(GetDlgItem(_hwnd, IdMessage), _message);
            UpdateWindow(_hwnd);

            string staged = DiscLibrary.EnsureStaged(path, log: Log.Add);
            var config = UserConfig.Load();
            config.DiscPath = staged;
            config.Save();
            Log.Add($"saved to {UserConfig.FilePath}");
            _result = staged;
            DestroyWindow(_hwnd);
            return;
        }

        Log.Add($"result: {result.Status} - {result.Message}");
        SetWindowTextW(GetDlgItem(_hwnd, IdStatus), StatusText());
        SetWindowTextW(GetDlgItem(_hwnd, IdMessage), _message);
        SetWindowTextW(GetDlgItem(_hwnd, IdLog), string.Join(Environment.NewLine, Log));
        var list = GetDlgItem(_hwnd, IdCandidates);
        SendMessageW(list, LB_RESETCONTENT, 0, 0);
        foreach (string c in _candidates) SendMessageW(list, LB_ADDSTRING, 0, c);
        Layout();
        UpdateWindow(_hwnd);
    }

    private static string StatusText() => _status switch
    {
        DiscStatus.Checking => "Status: Checking...",
        DiscStatus.Valid => "Status: Valid SLUS-01068",
        DiscStatus.Invalid => "Status: Invalid",
        DiscStatus.NeedsChoice => "Status: Choose an image",
        _ => "Status: Not added",
    };

    private static uint StatusColor() => _status switch
    {
        DiscStatus.Checking => Rgb(255, 214, 102),
        DiscStatus.Valid => Rgb(102, 224, 122),
        DiscStatus.Invalid => Rgb(236, 106, 106),
        DiscStatus.NeedsChoice => Rgb(255, 214, 102),
        _ => Rgb(154, 163, 174),
    };

    private static uint Rgb(int r, int g, int b) => (uint)(r | g << 8 | b << 16);

    private static string Version =>
        System.Reflection.Assembly.GetEntryAssembly()?.GetName().Version?.ToString(3) ?? "0.0.0";

    private static string? OpenFile(nint owner)
    {
        var ofn = new OpenFileName
        {
            StructSize = Marshal.SizeOf<OpenFileName>(),
            Owner = owner,
            Filter = "Disc images\0*.cue;*.bin;*.iso\0All files\0*.*\0",
            File = new string('\0', 512),
            MaxFile = 512,
            Title = "Choose your NASCAR Rumble disc image",
            Flags = 0x00000004 | 0x00080000,
        };
        return GetOpenFileNameW(ref ofn) ? ofn.File.TrimEnd('\0') : null;
    }

    private static string? OpenFolder(nint owner)
    {
        nint pidl = SHBrowseForFolderW(new BrowseInfo
        {
            Owner = owner,
            Title = "Choose the folder holding your NASCAR Rumble dump",
            Flags = 0x00000040,
        });
        if (pidl == 0) return null;
        var buffer = new char[260];
        bool ok = SHGetPathFromIDListW(pidl, buffer);
        Marshal.FreeCoTaskMem(pidl);
        return ok ? new string(buffer).TrimEnd('\0') : null;
    }

    private static void CopyToClipboard(string text)
    {
        if (!OpenClipboard(_hwnd)) return;
        EmptyClipboard();
        int bytes = (text.Length + 1) * 2;
        nint handle = GlobalAlloc(0x0042, (nint)bytes);
        if (handle != 0)
        {
            nint locked = GlobalLock(handle);
            if (locked != 0)
            {
                Marshal.Copy(text.ToCharArray(), 0, locked, text.Length);
                Marshal.WriteInt16(locked + text.Length * 2, 0);
                GlobalUnlock(handle);
                SetClipboardData(13, handle);
            }
        }
        CloseClipboard();
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Rect { public int Left, Top, Right, Bottom; }

    [StructLayout(LayoutKind.Sequential)]
    private sealed class DrawItemStruct
    {
        public int CtlType;
        public int CtlID;
        public int ItemID;
        public int ItemAction;
        public int ItemState;
        public nint HwndItem;
        public nint Hdc;
        public int RcLeft, RcTop, RcRight, RcBottom;
        public nint ItemData;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct WndClassEx
    {
        public int Size;
        public uint Style;
        public nint WndProc;
        public int ClsExtra;
        public int WndExtra;
        public nint Instance;
        public nint Icon;
        public nint Cursor;
        public nint Background;
        public string? MenuName;
        public string ClassName;
        public nint IconSm;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct OpenFileName
    {
        public int StructSize;
        public nint Owner;
        public nint Instance;
        public string Filter;
        public string CustomFilter;
        public int MaxCustomFilter;
        public int FilterIndex;
        public string File;
        public int MaxFile;
        public string FileTitle;
        public int MaxFileTitle;
        public string InitialDir;
        public string Title;
        public int Flags;
        public short FileOffset;
        public short FileExtension;
        public string DefExt;
        public nint CustData;
        public nint Hook;
        public string TemplateName;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private sealed class BrowseInfo
    {
        public nint Owner;
        public nint Root;
        public nint DisplayName;
        public string? Title;
        public int Flags;
        public nint Callback;
        public nint Param;
        public int Image;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Msg
    {
        public nint Hwnd;
        public uint Message;
        public nint WParam;
        public nint LParam;
        public uint Time;
        public int X;
        public int Y;
    }

    private static int MulDiv(int n, int d, int scale) => n * d / scale;

    [DllImport("kernel32.dll")] private static extern nint GetModuleHandleW(nint module);
    [DllImport("user32.dll")] private static extern bool SetProcessDpiAwarenessContext(nint value);
    [DllImport("user32.dll", SetLastError = true)] private static extern ushort RegisterClassExW(ref WndClassEx wc);
    [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Unicode)] private static extern nint CreateWindowExW(uint ex, string cls, string? title, uint style,
        int x, int y, int w, int h, nint parent, nint menu, nint instance, nint param);
    [DllImport("user32.dll")] private static extern nint DefWindowProcW(nint hwnd, uint msg, nint wParam, nint lParam);
    [DllImport("user32.dll")] private static extern bool ShowWindow(nint hwnd, int cmd);
    [DllImport("user32.dll")] private static extern bool UpdateWindow(nint hwnd);
    [DllImport("user32.dll")] private static extern bool DestroyWindow(nint hwnd);
    [DllImport("user32.dll")] private static extern int GetMessageW(out Msg msg, nint hwnd, uint min, uint max);
    [DllImport("user32.dll")] private static extern bool TranslateMessage(ref Msg msg);
    [DllImport("user32.dll")] private static extern nint DispatchMessageW(ref Msg msg);
    [DllImport("user32.dll")] private static extern void PostQuitMessage(int code);
    [DllImport("user32.dll")] private static extern nint GetDlgItem(nint hwnd, int id);
    [DllImport("user32.dll")] private static extern bool SetWindowPos(nint hwnd, nint after, int x, int y, int cx, int cy, uint flags);
    [DllImport("user32.dll")] private static extern bool GetClientRect(nint hwnd, out Rect rect);
    [DllImport("user32.dll")] private static extern int GetWindowLongPtrW(nint hwnd, int index);
    [DllImport("user32.dll")] private static extern nint SendMessageW(nint hwnd, uint msg, nint wParam, nint lParam);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern nint SendMessageW(nint hwnd, uint msg, nint wParam, string lParam);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern bool SetWindowTextW(nint hwnd, string text);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetWindowTextW(nint hwnd, StringBuilder text, int count);
    [DllImport("user32.dll")] private static extern uint GetDpiForWindow(nint hwnd);
    [DllImport("user32.dll")] private static extern int GetSystemMetrics(int index);
    [DllImport("user32.dll")] private static extern nint LoadIconW(nint instance, nint id);
    [DllImport("gdi32.dll")] private static extern int SetBkMode(nint dc, int mode);
    [DllImport("gdi32.dll")] private static extern uint SetTextColor(nint dc, uint color);
    [DllImport("gdi32.dll")] private static extern uint SetBkColor(nint dc, uint color);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int DrawTextW(nint dc, StringBuilder text, int count, ref Rect rect, uint format);
    [DllImport("user32.dll")] private static extern bool FillRect(nint dc, ref Rect rect, nint brush);
    [DllImport("user32.dll")] private static extern bool DrawFocusRect(nint dc, ref Rect rect);
    [DllImport("gdi32.dll")] private static extern nint CreateSolidBrush(uint color);
    [DllImport("gdi32.dll")] private static extern bool DeleteObject(nint obj);
    [DllImport("gdi32.dll")] private static extern nint SelectObject(nint dc, nint obj);
    [DllImport("gdi32.dll", CharSet = CharSet.Unicode)] private static extern nint CreateFontW(int height, int width, int esc, int orient,
        int weight, uint italic, uint underline, uint strike, uint charset, uint outPrec, uint clipPrec, uint quality, uint pitch, string face);

    [DllImport("comdlg32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool GetOpenFileNameW(ref OpenFileName ofn);
    [DllImport("shell32.dll", CharSet = CharSet.Unicode)] private static extern nint SHBrowseForFolderW(BrowseInfo info);
    [DllImport("shell32.dll", CharSet = CharSet.Unicode)] private static extern bool SHGetPathFromIDListW(nint pidl, char[] path);
    [DllImport("shell32.dll")] private static extern void DragAcceptFiles(nint hwnd, bool accept);
    [DllImport("shell32.dll", CharSet = CharSet.Unicode)] private static extern int DragQueryFileW(nint hDrop, uint index, char[]? buffer, int length);
    [DllImport("shell32.dll")] private static extern void DragFinish(nint hDrop);
    [DllImport("user32.dll")] private static extern bool OpenClipboard(nint owner);
    [DllImport("user32.dll")] private static extern bool CloseClipboard();
    [DllImport("user32.dll")] private static extern bool EmptyClipboard();
    [DllImport("user32.dll")] private static extern nint SetClipboardData(uint format, nint handle);
    [DllImport("kernel32.dll")] private static extern nint GlobalAlloc(uint flags, nint bytes);
    [DllImport("kernel32.dll")] private static extern nint GlobalLock(nint handle);
    [DllImport("kernel32.dll")] private static extern bool GlobalUnlock(nint handle);
}
