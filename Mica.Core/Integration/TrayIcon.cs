using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace Mica.Core.Integration;

/// <summary>One entry of the notification-area menu.</summary>
/// <param name="Text">Label, already localized. Ignored for a separator.</param>
/// <param name="Invoke">Runs when the entry is chosen.</param>
/// <param name="IsChecked">Draws a check mark, for entries that toggle something.</param>
/// <param name="IsSeparator">A divider rather than a command.</param>
public sealed record TrayMenuItem(
    string Text,
    Action? Invoke = null,
    bool IsChecked = false,
    bool IsSeparator = false)
{
    public static TrayMenuItem Separator { get; } = new(string.Empty, IsSeparator: true);
}

/// <summary>
/// A notification-area icon: the app's face while its window is closed.
/// <para>
/// Written against Shell_NotifyIcon directly rather than through a package, for the
/// same reason the shortcut and icon-resource code is: the surface needed here is
/// small and stable, while a UI dependency would have to keep pace with the Windows
/// App SDK.
/// </para>
/// <para>
/// The icon needs a real window to receive its callbacks — a message-only window
/// cannot be brought to the foreground, and a context menu that is not attached to
/// the foreground window refuses to close when the user clicks elsewhere.
/// </para>
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class TrayIcon : IDisposable
{
    private const int WmApp = 0x8000;
    private const int TrayCallback = WmApp + 1;

    private const int WmLeftButtonUp = 0x0202;
    private const int WmLeftButtonDoubleClick = 0x0203;
    private const int WmRightButtonUp = 0x0205;
    private const int WmNull = 0x0000;

    private const uint WsExToolWindow = 0x00000080;
    private const uint WsPopup = 0x80000000;
    private const int SwShowNoActivate = 4;

    private readonly WindowProcedure _procedure;
    private readonly string _className;
    private readonly ushort _classAtom;

    private IntPtr _window;
    private IntPtr _icon;
    private IReadOnlyList<TrayMenuItem> _menu = [];
    private bool _visible;
    private bool _disposed;

    /// <summary>Raised on a left click or double click — the "show me the app" gesture.</summary>
    public event EventHandler? Activated;

    public TrayIcon(string tooltip, string iconPath)
    {
        _procedure = HandleMessage;
        _className = "MicaTrayIcon_" + Guid.NewGuid().ToString("N");

        var moduleHandle = GetModuleHandle(null);

        var windowClass = new WindowClass
        {
            Size = (uint)Marshal.SizeOf<WindowClass>(),
            Procedure = Marshal.GetFunctionPointerForDelegate(_procedure),
            Instance = moduleHandle,
            ClassName = _className,
        };

        _classAtom = RegisterClassEx(ref windowClass);
        if (_classAtom == 0)
        {
            throw new InvalidOperationException(
                $"Registering the tray window class failed ({Marshal.GetLastWin32Error()}).");
        }

        // Parked off-screen rather than hidden, and a tool window so it stays out of
        // Alt+Tab and the taskbar. It has to be a window the shell will accept as
        // foreground: SetForegroundWindow fails on a window that was never shown, and
        // a menu whose owner is not the foreground window refuses to close when the
        // user clicks away from it.
        // Untitled on purpose: a titled top-level window gets picked up as the
        // process's main window by anything walking the window list, which would put
        // this 1×1 placeholder forward instead of the app's real window.
        _window = CreateWindowEx(
            WsExToolWindow, _className, string.Empty, WsPopup,
            -32000, -32000, 1, 1,
            IntPtr.Zero, IntPtr.Zero, moduleHandle, IntPtr.Zero);

        if (_window == IntPtr.Zero)
        {
            throw new InvalidOperationException(
                $"Creating the tray window failed ({Marshal.GetLastWin32Error()}).");
        }

        ShowWindow(_window, SwShowNoActivate);

        _icon = LoadIconFromFile(iconPath);

        var data = CreateData();
        data.Flags = NotifyIconFlags.Message | NotifyIconFlags.Icon | NotifyIconFlags.Tip;
        data.CallbackMessage = TrayCallback;
        data.Icon = _icon;
        data.Tip = Cap(tooltip, 127);

        _visible = Shell_NotifyIcon(NotifyIconMessage.Add, ref data);
    }

    /// <summary>Replaces the context menu. Called whenever a toggle's state changes.</summary>
    public void SetMenu(IReadOnlyList<TrayMenuItem> items) => _menu = items;

    public void SetTooltip(string tooltip)
    {
        if (!_visible)
        {
            return;
        }

        var data = CreateData();
        data.Flags = NotifyIconFlags.Tip;
        data.Tip = Cap(tooltip, 127);
        Shell_NotifyIcon(NotifyIconMessage.Modify, ref data);
    }

    /// <summary>
    /// Shows a balloon. Used once, the first time a window closes into the tray, so
    /// the app does not simply appear to vanish.
    /// </summary>
    public void ShowBalloon(string title, string message)
    {
        if (!_visible)
        {
            return;
        }

        var data = CreateData();
        data.Flags = NotifyIconFlags.Info;
        data.InfoTitle = Cap(title, 63);
        data.Info = Cap(message, 255);
        data.InfoFlags = 0x00000001; // NIIF_INFO
        Shell_NotifyIcon(NotifyIconMessage.Modify, ref data);
    }

    private NotifyIconData CreateData() => new()
    {
        Size = (uint)Marshal.SizeOf<NotifyIconData>(),
        Window = _window,
        Id = 1,
    };

    private IntPtr HandleMessage(IntPtr window, uint message, IntPtr wParam, IntPtr lParam)
    {
        if (message == TrayCallback)
        {
            switch ((int)lParam)
            {
                case WmLeftButtonUp:
                case WmLeftButtonDoubleClick:
                    Activated?.Invoke(this, EventArgs.Empty);
                    return IntPtr.Zero;

                case WmRightButtonUp:
                    ShowMenu();
                    return IntPtr.Zero;
            }
        }
        // No PostQuitMessage on WM_DESTROY. This window shares the application's UI
        // thread and its message loop: posting WM_QUIT there ends the loop and takes
        // the whole app down. Turning the setting off disposes this icon, so that
        // path is reached in normal use, not only at shutdown.
        return DefWindowProc(window, message, wParam, lParam);
    }

    private void ShowMenu()
    {
        if (_menu.Count == 0)
        {
            return;
        }

        var menu = CreatePopupMenu();
        if (menu == IntPtr.Zero)
        {
            return;
        }

        try
        {
            for (var i = 0; i < _menu.Count; i++)
            {
                var item = _menu[i];

                if (item.IsSeparator)
                {
                    AppendMenu(menu, MenuFlags.Separator, IntPtr.Zero, null);
                    continue;
                }

                var flags = MenuFlags.String | (item.IsChecked ? MenuFlags.Checked : MenuFlags.Unchecked);
                AppendMenu(menu, flags, (IntPtr)(i + 1), item.Text);
            }

            GetCursorPos(out var cursor);

            // Without this the menu stays on screen after a click elsewhere: the shell
            // only dismisses menus belonging to the foreground window.
            SetForegroundWindow(_window);

            var chosen = TrackPopupMenuEx(
                menu,
                TrackPopupFlags.RightButton | TrackPopupFlags.ReturnCommand | TrackPopupFlags.NonNotify,
                cursor.X,
                cursor.Y,
                _window,
                IntPtr.Zero);

            // Companion to the same quirk, per the documented workaround.
            PostMessage(_window, WmNull, IntPtr.Zero, IntPtr.Zero);

            if (chosen > 0 && chosen <= _menu.Count)
            {
                _menu[chosen - 1].Invoke?.Invoke();
            }
        }
        finally
        {
            DestroyMenu(menu);
        }
    }

    private static IntPtr LoadIconFromFile(string path)
    {
        if (!File.Exists(path))
        {
            return IntPtr.Zero;
        }

        // The notification area asks for a small icon; letting the loader pick the
        // matching entry keeps it crisp instead of downscaling the 256 px one.
        return LoadImage(
            IntPtr.Zero,
            path,
            1, // IMAGE_ICON
            GetSystemMetrics(49), // SM_CXSMICON
            GetSystemMetrics(50), // SM_CYSMICON
            0x00000010); // LR_LOADFROMFILE
    }

    private static string Cap(string value, int maxLength) =>
        string.IsNullOrEmpty(value) || value.Length <= maxLength ? value : value[..maxLength];

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        if (_visible)
        {
            var data = CreateData();
            Shell_NotifyIcon(NotifyIconMessage.Delete, ref data);
            _visible = false;
        }

        if (_icon != IntPtr.Zero)
        {
            DestroyIcon(_icon);
            _icon = IntPtr.Zero;
        }

        if (_window != IntPtr.Zero)
        {
            DestroyWindow(_window);
            _window = IntPtr.Zero;
        }

        if (_classAtom != 0)
        {
            UnregisterClass(_className, GetModuleHandle(null));
        }
    }

    // ---- Interop -------------------------------------------------------------

    private delegate IntPtr WindowProcedure(IntPtr window, uint message, IntPtr wParam, IntPtr lParam);

    [Flags]
    private enum NotifyIconFlags : uint
    {
        Message = 0x01,
        Icon = 0x02,
        Tip = 0x04,
        Info = 0x10,
    }

    private enum NotifyIconMessage : uint
    {
        Add = 0x00,
        Modify = 0x01,
        Delete = 0x02,
    }

    [Flags]
    private enum MenuFlags : uint
    {
        String = 0x0000,
        Unchecked = 0x0000,
        Checked = 0x0008,
        Separator = 0x0800,
    }

    [Flags]
    private enum TrackPopupFlags : uint
    {
        RightButton = 0x0002,
        ReturnCommand = 0x0100,
        NonNotify = 0x0080,
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Point
    {
        public int X;
        public int Y;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct WindowClass
    {
        public uint Size;
        public uint Style;
        public IntPtr Procedure;
        public int ClassExtra;
        public int WindowExtra;
        public IntPtr Instance;
        public IntPtr Icon;
        public IntPtr Cursor;
        public IntPtr Background;
        [MarshalAs(UnmanagedType.LPWStr)]
        public string? MenuName;
        [MarshalAs(UnmanagedType.LPWStr)]
        public string ClassName;
        public IntPtr SmallIcon;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct NotifyIconData
    {
        public uint Size;
        public IntPtr Window;
        public uint Id;
        public NotifyIconFlags Flags;
        public uint CallbackMessage;
        public IntPtr Icon;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
        public string Tip;
        public uint State;
        public uint StateMask;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)]
        public string Info;
        public uint Version;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)]
        public string InfoTitle;
        public uint InfoFlags;
        public Guid Item;
        public IntPtr BalloonIcon;
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern bool Shell_NotifyIcon(NotifyIconMessage message, ref NotifyIconData data);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern ushort RegisterClassEx(ref WindowClass windowClass);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool UnregisterClass(string className, IntPtr instance);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr CreateWindowEx(
        uint exStyle, string className, string windowName, uint style,
        int x, int y, int width, int height,
        IntPtr parent, IntPtr menu, IntPtr instance, IntPtr param);

    [DllImport("user32.dll")]
    private static extern bool DestroyWindow(IntPtr window);

    [DllImport("user32.dll")]
    private static extern bool ShowWindow(IntPtr window, int command);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr DefWindowProc(IntPtr window, uint message, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern bool PostMessage(IntPtr window, uint message, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern IntPtr CreatePopupMenu();

    [DllImport("user32.dll")]
    private static extern bool DestroyMenu(IntPtr menu);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern bool AppendMenu(IntPtr menu, MenuFlags flags, IntPtr item, string? text);

    [DllImport("user32.dll")]
    private static extern int TrackPopupMenuEx(
        IntPtr menu, TrackPopupFlags flags, int x, int y, IntPtr window, IntPtr parameters);

    [DllImport("user32.dll")]
    private static extern bool GetCursorPos(out Point point);

    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr window);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr LoadImage(
        IntPtr instance, string name, uint type, int width, int height, uint load);

    [DllImport("user32.dll")]
    private static extern bool DestroyIcon(IntPtr icon);

    [DllImport("user32.dll")]
    private static extern int GetSystemMetrics(int index);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr GetModuleHandle(string? moduleName);
}
