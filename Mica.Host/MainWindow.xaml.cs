using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media.Imaging;
using Mica.Core;
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

        Closed += OnClosed;
    }

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
        if (string.IsNullOrEmpty(_config.Slug))
        {
            return;
        }

        try
        {
            var size = AppWindow.Size;
            if (size.Width > 400 && size.Height > 300)
            {
                _config.WindowWidth = size.Width;
                _config.WindowHeight = size.Height;
                _config.Save(MicaPaths.ConfigFile(_config.Slug));
            }
        }
        catch (Exception ex)
        {
            StartupLog.WriteException("Window.Closed", ex);
        }
    }
}
