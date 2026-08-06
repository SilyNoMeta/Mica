using Microsoft.UI.Xaml;
using Mica.Core;
using Mica.Core.Apps;
using Mica.Core.Integration;
using Mica.Core.Localization;

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

        // Settings › Installed apps calls back into the builder to remove one app;
        // the uninstaller calls back to remove all of them. Neither path shows UI.
        for (var i = 1; i < argv.Length; i++)
        {
            if (argv[i] == "--uninstall-all")
            {
                Run(AppRegistry.RemoveAll);
                Environment.Exit(0);
                return;
            }

            if (argv[i] == "--uninstall" && i + 1 < argv.Length)
            {
                var slug = argv[i + 1];
                Run(() =>
                {
                    if (AppRegistry.Find(slug) is { } config)
                    {
                        AppRegistry.Remove(config);
                    }
                });

                Environment.Exit(0);
                return;
            }
        }

        Strings.ApplyLanguageOverride(argv);

        Directory.CreateDirectory(MicaPaths.Root);
        StartupLog.Initialize(Path.Combine(MicaPaths.Root, "builder.log"));

        StartupLog.Write($"UI reading direction: {Strings.Get("FlowDirection")}");

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

    /// <summary>Runs a silent maintenance action; nothing can be reported from one.</summary>
    private static void Run(Action action)
    {
        try
        {
            action();
        }
        catch
        {
            // An uninstall that cannot finish must still let the uninstaller proceed.
        }
    }
}
