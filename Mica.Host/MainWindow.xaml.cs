using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media.Imaging;
using Microsoft.UI.Windowing;
using Mica.Core;
using Mica.Core.Apps;
using Mica.Core.Integration;
using Mica.Core.Localization;
using Windows.Graphics;

namespace Mica.Host;

/// <summary>
/// The window of a generated app. Everything specific to the site — title, icon,
/// size — comes from the app's configuration, so this window is identical for
/// every app the user creates.
/// </summary>
public sealed partial class MainWindow : Window
{
    private readonly WebAppConfig _config = App.Config;

    private TrayIcon? _tray;
    private bool _quitting;
    private bool _announcedTray;

    public MainWindow()
    {
        InitializeComponent();
        StartupLog.Write("MainWindow created");

        ExtendsContentIntoTitleBar = true;
        SetTitleBar(AppTitleBar);
        AppTitleBar.Title = _config.Name;
        Title = _config.Name;

        var icon = MicaPaths.IconFile(_config.Slug);
        if (File.Exists(icon))
        {
            AppWindow.SetIcon(icon);
            _ = ShowTitleBarIconAsync(icon);
        }

        AppWindow.Resize(new SizeInt32(_config.WindowWidth, _config.WindowHeight));

        // Mirrors the title bar for right-to-left languages; the shell mirrors its
        // own toolbar and deliberately leaves the web content alone.
        Root.FlowDirection = Strings.FlowDirection;

        // The title stays the app's name rather than following the page: this is an
        // app, not a browser tab, and the name is what the taskbar and docks show.
        Shell.Configure(_config);
        Shell.BackgroundModeChanged += (_, _) => UpdateTray();

        AppWindow.Closing += OnClosing;
        Closed += OnClosed;

        UpdateTray();
    }

    // ---- Notification area ---------------------------------------------------

    /// <summary>
    /// Brings the app back from the notification area, or to the front if it is
    /// already open. Also what a second launch of the same app ends up calling.
    /// </summary>
    public void Restore()
    {
        AppWindow.Show();
        Activate();

        // Show() alone leaves the window behind whatever had focus.
        SetForegroundWindow(WinRT.Interop.WindowNative.GetWindowHandle(this));
    }

    /// <summary>
    /// Creates or removes the tray icon to match the setting, and refreshes its menu
    /// so the "start with Windows" tick reflects reality — including a change the
    /// user made in Windows' own startup settings.
    /// </summary>
    public void UpdateTray()
    {
        if (!_config.RunInBackground)
        {
            _tray?.Dispose();
            _tray = null;
            return;
        }

        try
        {
            if (_tray is null)
            {
                _tray = new TrayIcon(_config.Name, MicaPaths.IconFile(_config.Slug));
                _tray.Activated += (_, _) => DispatcherQueue.TryEnqueue(Restore);
            }

            _tray.SetMenu(
            [
                new TrayMenuItem(Strings.Get("TrayOpen"), () => DispatcherQueue.TryEnqueue(Restore)),
                new TrayMenuItem(
                    Strings.Get("TrayStartWithWindows"),
                    ToggleStartWithWindows,
                    IsChecked: StartupRegistration.IsEnabled(_config)),
                TrayMenuItem.Separator,
                new TrayMenuItem(Strings.Get("TrayQuit"), () => DispatcherQueue.TryEnqueue(Quit)),
            ]);
        }
        catch (Exception ex)
        {
            // Without an icon the app would vanish on close with no way back, so fall
            // back to quitting normally.
            StartupLog.WriteException("Tray.Create", ex);
            _tray = null;
            _config.RunInBackground = false;
        }
    }

    private void ToggleStartWithWindows()
    {
        var enabled = !StartupRegistration.IsEnabled(_config);
        StartupRegistration.Set(_config, enabled);
        _config.StartWithWindows = enabled;
        SaveConfig();

        DispatcherQueue.TryEnqueue(UpdateTray);
    }

    private void Quit()
    {
        _quitting = true;
        _tray?.Dispose();
        _tray = null;
        Close();
    }

    /// <summary>
    /// Closing the window hides it instead of quitting, when the user asked for that.
    /// The page stays loaded, which is the whole point: a site can only raise a
    /// notification while it is running.
    /// </summary>
    private void OnClosing(Microsoft.UI.Windowing.AppWindow sender, AppWindowClosingEventArgs args)
    {
        if (_quitting || !_config.RunInBackground || _tray is null)
        {
            return;
        }

        args.Cancel = true;
        AppWindow.Hide();

        if (!_announcedTray)
        {
            _announcedTray = true;
            _tray.ShowBalloon(_config.Name, Strings.Format("TrayStillRunning", _config.Name));
        }
    }

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr window);

    /// <summary>
    /// Shows the site's icon in the title bar. It has to be loaded through a stream:
    /// WinUI's BitmapImage does not read plain file paths, and an unpackaged app has
    /// no ms-appdata scheme to fall back on.
    /// </summary>
    private async Task ShowTitleBarIconAsync(string path)
    {
        try
        {
            var file = await Windows.Storage.StorageFile.GetFileFromPathAsync(path);
            using var stream = await file.OpenReadAsync();

            var bitmap = new BitmapImage();
            await bitmap.SetSourceAsync(stream);

            AppTitleBar.IconSource = new Microsoft.UI.Xaml.Controls.ImageIconSource
            {
                ImageSource = bitmap,
            };
        }
        catch (Exception ex)
        {
            StartupLog.WriteException("TitleBar.Icon", ex);
        }
    }

    /// <summary>Remembers the window size so the app reopens the way it was left.</summary>
    private void OnClosed(object sender, WindowEventArgs args)
    {
        var size = AppWindow.Size;
        if (size.Width > 400 && size.Height > 300)
        {
            _config.WindowWidth = size.Width;
            _config.WindowHeight = size.Height;
        }

        SaveConfig();

        _tray?.Dispose();
        _tray = null;
    }

    private void SaveConfig()
    {
        if (string.IsNullOrEmpty(_config.Slug))
        {
            return;
        }

        try
        {
            _config.Save(MicaPaths.ConfigFile(_config.Slug));
        }
        catch (Exception ex)
        {
            StartupLog.WriteException("Window.SaveConfig", ex);
        }
    }
}
