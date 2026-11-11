using System.Runtime.InteropServices;

namespace BtManage.Tray;

/// <summary>
/// The notification area icon. Messages arrive at the window passed to the constructor, which
/// this class subclasses for as long as it lives.
/// </summary>
internal sealed class TrayIcon : IDisposable
{
    private const uint IconId = 1;
    private const uint CallbackMessage = WM_APP + 1;

    private const uint WM_APP = 0x8000;
    private const uint WM_USER = 0x0400;
    private const uint WM_SETTINGCHANGE = 0x001A;
    private const uint WM_CONTEXTMENU = 0x007B;
    private const uint WM_NULL = 0x0000;
    private const uint NIN_SELECT = WM_USER;
    private const uint NIN_KEYSELECT = WM_USER + 1;

    private const uint NIM_ADD = 0;
    private const uint NIM_MODIFY = 1;
    private const uint NIM_DELETE = 2;
    private const uint NIM_SETVERSION = 4;
    private const uint NIF_MESSAGE = 0x01;
    private const uint NIF_ICON = 0x02;
    private const uint NIF_TIP = 0x04;
    private const uint NIF_SHOWTIP = 0x80;
    private const uint NOTIFYICON_VERSION_4 = 4;

    private const int SM_CXSMICON = 49;
    private const uint MF_STRING = 0x0000;
    private const uint MF_SEPARATOR = 0x0800;
    private const uint TPM_RIGHTBUTTON = 0x0002;
    private const uint TPM_RETURNCMD = 0x0100;
    private const uint TPM_BOTTOMALIGN = 0x0020;
    private const nuint MenuSettings = 1;
    private const nuint MenuExit = 2;

    private readonly nint _window;
    private readonly SubclassProc _procedure;
    private readonly uint _taskbarCreated;
    private nint _icon;
    private string _tooltip = Strings.Get("AppName");

    public TrayIcon(nint window)
    {
        _window = window;
        _procedure = WindowProcedure;
        _taskbarCreated = RegisterWindowMessageW("TaskbarCreated");
        SetWindowSubclass(_window, _procedure, IconId, 0);
        Add();
    }

    /// <summary>The icon was clicked or chosen with the keyboard.</summary>
    public event Action? Activated;

    public event Action? SettingsRequested;

    public event Action? ExitRequested;

    public void SetTooltip(string tooltip)
    {
        if (tooltip == _tooltip)
        {
            return;
        }

        _tooltip = tooltip;
        var data = Data(NIF_TIP | NIF_SHOWTIP);
        Shell_NotifyIconW(NIM_MODIFY, ref data);
    }

    public void Dispose()
    {
        var data = Data(0);
        Shell_NotifyIconW(NIM_DELETE, ref data);
        RemoveWindowSubclass(_window, _procedure, IconId);
        ReleaseIcon();
    }

    private void Add()
    {
        ReleaseIcon();
        _icon = TrayGlyph.Render(GetSystemMetrics(SM_CXSMICON));

        var data = Data(NIF_MESSAGE | NIF_ICON | NIF_TIP | NIF_SHOWTIP);
        Shell_NotifyIconW(NIM_ADD, ref data);
        data.uVersion = NOTIFYICON_VERSION_4;
        Shell_NotifyIconW(NIM_SETVERSION, ref data);
    }

    private void RefreshIcon()
    {
        var previous = _icon;
        _icon = TrayGlyph.Render(GetSystemMetrics(SM_CXSMICON));
        var data = Data(NIF_ICON);
        Shell_NotifyIconW(NIM_MODIFY, ref data);
        if (previous != 0)
        {
            DestroyIcon(previous);
        }
    }

    private void ReleaseIcon()
    {
        if (_icon != 0)
        {
            DestroyIcon(_icon);
            _icon = 0;
        }
    }

    private NOTIFYICONDATAW Data(uint flags) => new()
    {
        cbSize = (uint)Marshal.SizeOf<NOTIFYICONDATAW>(),
        hWnd = _window,
        uID = IconId,
        uFlags = flags,
        uCallbackMessage = CallbackMessage,
        hIcon = _icon,
        // The shell cuts the tooltip at 127 characters.
        szTip = _tooltip.Length > 127 ? _tooltip[..127] : _tooltip,
        szInfo = string.Empty,
        szInfoTitle = string.Empty,
    };

    private nint WindowProcedure(nint window, uint message, nint wParam, nint lParam, nuint id, nuint data)
    {
        if (message == CallbackMessage)
        {
            // With NOTIFYICON_VERSION_4 the event is in the low word of lParam.
            switch ((uint)(lParam & 0xFFFF))
            {
                case NIN_SELECT:
                case NIN_KEYSELECT:
                    Activated?.Invoke();
                    break;
                case WM_CONTEXTMENU:
                    ShowMenu();
                    break;
            }

            return 0;
        }

        if (message == _taskbarCreated)
        {
            // Explorer restarted and lost every icon.
            Add();
        }
        else if (message == WM_SETTINGCHANGE && Marshal.PtrToStringUni(lParam) == "ImmersiveColorSet")
        {
            // The taskbar may have switched between light and dark.
            RefreshIcon();
        }

        return DefSubclassProc(window, message, wParam, lParam);
    }

    private void ShowMenu()
    {
        var menu = CreatePopupMenu();
        try
        {
            AppendMenuW(menu, MF_STRING, MenuSettings, Strings.Get("BluetoothSettings"));
            AppendMenuW(menu, MF_SEPARATOR, 0, null);
            AppendMenuW(menu, MF_STRING, MenuExit, Strings.Get("MenuExit"));

            GetCursorPos(out var cursor);
            // The menu closes on a click elsewhere only if its owner is the foreground window.
            SetForegroundWindow(_window);
            var command = (nuint)TrackPopupMenuEx(menu, TPM_RIGHTBUTTON | TPM_RETURNCMD | TPM_BOTTOMALIGN, cursor.X, cursor.Y, _window, 0);
            PostMessageW(_window, WM_NULL, 0, 0);

            switch (command)
            {
                case MenuSettings:
                    SettingsRequested?.Invoke();
                    break;
                case MenuExit:
                    ExitRequested?.Invoke();
                    break;
            }
        }
        finally
        {
            DestroyMenu(menu);
        }
    }

    private delegate nint SubclassProc(nint window, uint message, nint wParam, nint lParam, nuint id, nuint data);

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT
    {
        public int X;
        public int Y;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct NOTIFYICONDATAW
    {
        public uint cbSize;
        public nint hWnd;
        public uint uID;
        public uint uFlags;
        public uint uCallbackMessage;
        public nint hIcon;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
        public string szTip;
        public uint dwState;
        public uint dwStateMask;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)]
        public string szInfo;
        public uint uVersion;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)]
        public string szInfoTitle;
        public uint dwInfoFlags;
        public Guid guidItem;
        public nint hBalloonIcon;
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool Shell_NotifyIconW(uint message, ref NOTIFYICONDATAW data);

    [DllImport("comctl32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetWindowSubclass(nint window, SubclassProc procedure, nuint id, nuint data);

    [DllImport("comctl32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool RemoveWindowSubclass(nint window, SubclassProc procedure, nuint id);

    [DllImport("comctl32.dll")]
    private static extern nint DefSubclassProc(nint window, uint message, nint wParam, nint lParam);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern uint RegisterWindowMessageW(string name);

    [DllImport("user32.dll")]
    private static extern int GetSystemMetrics(int index);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DestroyIcon(nint icon);

    [DllImport("user32.dll")]
    private static extern nint CreatePopupMenu();

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool AppendMenuW(nint menu, uint flags, nuint id, string? text);

    [DllImport("user32.dll")]
    private static extern int TrackPopupMenuEx(nint menu, uint flags, int x, int y, nint window, nint parameters);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DestroyMenu(nint menu);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetCursorPos(out POINT point);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetForegroundWindow(nint window);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool PostMessageW(nint window, uint message, nint wParam, nint lParam);
}
