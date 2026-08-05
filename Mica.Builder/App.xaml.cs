using Microsoft.UI.Xaml;
using Mica.Core;
using Mica.Core.Apps;
using Mica.Core.Integration;

namespace Mica.Builder;

/// <summary>
/// The Mica window itself: where the user sees their apps and creates new ones.
/// </summary>
public partial class App : Application
{
    public static Window Window { get; private set; } = null!;

    public static nint WindowHandle => WinRT.Interop.WindowNative.GetWindowHandle(Window);

    public App() => InitializeComponent();

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        var argv = Environment.GetCommandLineArgs();

        // Settings › Installed apps calls back into the builder to remove an app.
        // That path never shows UI.
        for (var i = 1; i < argv.Length - 1; i++)
        {
            if (argv[i] == "--uninstall")
            {
                RemoveSilently(argv[i + 1]);
                Exit();
                return;
            }
        }

        Directory.CreateDirectory(MicaPaths.Root);
        StartupLog.Initialize(Path.Combine(MicaPaths.Root, "builder.log"));

        var appUserModelId = $"{MicaPaths.ProductName}.Builder";
        AppIdentity.ApplyToProcess(appUserModelId);

        // Put Mica in the Start menu on every run: an unpackaged app cannot be found
        // by Start or search until a shortcut exists, and installing Mica is exactly
        // what the user did to get here.
        AppRegistry.EnsureBuilderShortcut();

        // Shortcuts hold an absolute path to the host, so a moved or rebuilt
        // installation would leave every app pointing at a path that is gone.
        AppRegistry.RepairAll();

        Window = new MainWindow();
        AppIdentity.ApplyToWindow(WindowHandle, appUserModelId);
        Window.Activate();
    }

    private static void RemoveSilently(string slug)
    {
        try
        {
            var config = AppRegistry.Find(slug);
            if (config is not null)
            {
                AppRegistry.Remove(config);
            }
        }
        catch
        {
            // Nothing can be reported from a silent uninstall.
        }
    }
}
