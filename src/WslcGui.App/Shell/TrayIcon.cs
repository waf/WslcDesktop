using System.Runtime.InteropServices;

using Aprillz.MewUI;
using Aprillz.MewUI.Controls;

namespace WslcGui.App.Shell;

/// <summary>
/// The notification-area icon: click to show the window, right-click for a menu. MewUI has no tray support, so this
/// talks to Shell_NotifyIcon directly and receives its callbacks through <see cref="Window.NativeMessage"/>.
/// </summary>
internal sealed unsafe partial class TrayIcon : IDisposable
{
    private const uint CallbackMessage = 0x8000 + 0x57; // WM_APP + n
    private const uint NIM_ADD = 0, NIM_MODIFY = 1, NIM_DELETE = 2, NIM_SETVERSION = 4;
    private const uint NIF_MESSAGE = 0x1, NIF_ICON = 0x2, NIF_TIP = 0x4, NIF_SHOWTIP = 0x80;
    private const uint NOTIFYICON_VERSION_4 = 4;
    private const uint WM_LBUTTONUP = 0x0202, WM_CONTEXTMENU = 0x007B, WM_RBUTTONUP = 0x0205;
    private const uint MF_STRING = 0x0, MF_SEPARATOR = 0x800, MF_GRAYED = 0x1;
    private const uint TPM_RETURNCMD = 0x100, TPM_RIGHTBUTTON = 0x2;
    private const uint IMAGE_ICON = 1, LR_DEFAULTCOLOR = 0;
    private const int IconResourceId = 32512; // the ApplicationIcon resource in the exe

    private readonly Window _window;
    private readonly Func<IReadOnlyList<TrayMenuItem>> _buildMenu;
    private readonly nint _icon;
    private bool _added;

    /// <param name="buildMenu">Called each time the menu opens.</param>
    public TrayIcon(Window window, string tooltip, Func<IReadOnlyList<TrayMenuItem>> buildMenu)
    {
        _window = window;
        _buildMenu = buildMenu;
        _icon = LoadImageW(GetModuleHandleW(null), IconResourceId, IMAGE_ICON, GetSystemMetrics(49), GetSystemMetrics(50), LR_DEFAULTCOLOR);
        if (_icon == 0)
        {
            _icon = LoadIconW(0, IconResourceId); // IDI_APPLICATION
        }

        _window.NativeMessage += OnNativeMessage;
        Tooltip = tooltip;
    }

    public string Tooltip { get; set; }

    /// <summary>Adds the icon. Call once the window has a handle.</summary>
    public void Show()
    {
        var data = CreateData(NIF_MESSAGE | NIF_ICON | NIF_TIP | NIF_SHOWTIP);
        _added = Shell_NotifyIconW(NIM_ADD, ref data);
        data.uTimeoutOrVersion = NOTIFYICON_VERSION_4;
        Shell_NotifyIconW(NIM_SETVERSION, ref data);
    }

    /// <summary>Updates the hover text.</summary>
    public void UpdateTooltip(string tooltip)
    {
        Tooltip = tooltip;
        if (_added)
        {
            var data = CreateData(NIF_TIP | NIF_SHOWTIP);
            Shell_NotifyIconW(NIM_MODIFY, ref data);
        }
    }

    public void Dispose()
    {
        _window.NativeMessage -= OnNativeMessage;
        if (_added)
        {
            var data = CreateData(0);
            Shell_NotifyIconW(NIM_DELETE, ref data);
            _added = false;
        }
    }

    private void OnNativeMessage(NativeMessageEventArgs e)
    {
        if (e is not Win32NativeMessageEventArgs message || message.Msg != CallbackMessage)
        {
            return;
        }

        // With NOTIFYICON_VERSION_4 the event is in the low word of lParam.
        switch ((uint)(message.LParam & 0xFFFF))
        {
            case WM_LBUTTONUP:
                ShowWindow();
                break;
            case WM_CONTEXTMENU:
            case WM_RBUTTONUP:
                ShowMenu();
                break;
        }

        e.Handled = true;
    }

    public void ShowWindow()
    {
        _window.Show();
        if (_window.WindowState == WindowState.Minimized)
        {
            _window.WindowState = WindowState.Normal;
        }

        _window.Activate();
    }

    private void ShowMenu()
    {
        var items = _buildMenu();
        var menu = CreatePopupMenu();
        try
        {
            for (var i = 0; i < items.Count; i++)
            {
                var item = items[i];
                if (item.Text is null)
                {
                    AppendMenuW(menu, MF_SEPARATOR, 0, null);
                }
                else
                {
                    AppendMenuW(menu, MF_STRING | (item.OnClick is null ? MF_GRAYED : 0), (nuint)(i + 1), item.Text);
                }
            }

            GetCursorPos(out var point);
            SetForegroundWindow(_window.Handle); // so the menu closes when clicking elsewhere
            var chosen = TrackPopupMenu(menu, TPM_RETURNCMD | TPM_RIGHTBUTTON, point.X, point.Y, 0, _window.Handle, 0);
            if (chosen > 0 && items[chosen - 1].OnClick is { } onClick)
            {
                Application.Current.Dispatcher!.BeginInvoke(onClick);
            }
        }
        finally
        {
            DestroyMenu(menu);
        }
    }

    private NOTIFYICONDATAW CreateData(uint flags)
    {
        var data = new NOTIFYICONDATAW
        {
            cbSize = (uint)sizeof(NOTIFYICONDATAW),
            hWnd = _window.Handle,
            uID = 1,
            uFlags = flags,
            uCallbackMessage = CallbackMessage,
            hIcon = _icon,
        };

        var tip = Tooltip.Length > 127 ? Tooltip[..127] : Tooltip;
        for (var i = 0; i < tip.Length; i++)
        {
            data.szTip[i] = tip[i];
        }

        return data;
    }

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
        public fixed char szTip[128];
        public uint dwState;
        public uint dwStateMask;
        public fixed char szInfo[256];
        public uint uTimeoutOrVersion;
        public fixed char szInfoTitle[64];
        public uint dwInfoFlags;
        public Guid guidItem;
        public nint hBalloonIcon;
    }

    [LibraryImport("shell32.dll", EntryPoint = "Shell_NotifyIconW")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool Shell_NotifyIconW(uint message, ref NOTIFYICONDATAW data);

    [LibraryImport("kernel32.dll", StringMarshalling = StringMarshalling.Utf16)]
    private static partial nint GetModuleHandleW(string? moduleName);

    [LibraryImport("user32.dll")]
    private static partial nint LoadImageW(nint instance, nint name, uint type, int cx, int cy, uint flags);

    [LibraryImport("user32.dll")]
    private static partial nint LoadIconW(nint instance, nint name);

    [LibraryImport("user32.dll")]
    private static partial int GetSystemMetrics(int index);

    [LibraryImport("user32.dll")]
    private static partial nint CreatePopupMenu();

    [LibraryImport("user32.dll", StringMarshalling = StringMarshalling.Utf16)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool AppendMenuW(nint menu, uint flags, nuint id, string? text);

    [LibraryImport("user32.dll")]
    private static partial int TrackPopupMenu(nint menu, uint flags, int x, int y, int reserved, nint hwnd, nint rect);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool DestroyMenu(nint menu);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GetCursorPos(out POINT point);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool SetForegroundWindow(nint hwnd);
}

/// <param name="Text">Null for a separator.</param>
/// <param name="OnClick">Null for a disabled (informational) item.</param>
internal sealed record TrayMenuItem(string? Text, Action? OnClick = null)
{
    public static TrayMenuItem Separator { get; } = new(Text: null);
}
