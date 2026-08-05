using Microsoft.UI.Xaml;
using Mica.Core;
using Mica.Core.Integration;

namespace Mica.Host;

/// <summary>
/// The generic host process. One executable serves every app the user creates:
/// which app to run comes from the <c>--app &lt;slug&gt;</c> argument on the
/// shortcut, and everything else from that app's <c>app.json</c>.
/// </summary>
public partial class App : Application
{
    public static Window Window { get; private set; } = null!;

    public static WebAppConfig Config { get; private set; } = null!;

    public static nint WindowHandle => WinRT.Interop.WindowNative.GetWindowHandle(Window);

    public App()
    {
        UnhandledException += (_, e) => StartupLog.WriteException("App.Unhandled", e.Exception);
        InitializeComponent();
    }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        try
        {
            Config = ResolveConfig(Environment.GetCommandLineArgs());
        }
        catch (Exception ex)
        {
            // Without a valid app there is nothing to show; fail quietly rather
            // than leaving an orphan process behind.
            StartupLog.WriteException("App.ResolveConfig", ex);
            Environment.Exit(1);
            return;
        }

        if (!string.IsNullOrEmpty(Config.Slug))
        {
            StartupLog.Initialize(MicaPaths.LogFile(Config.Slug));
        }

        StartupLog.Write($"Launching {Config.Name} ({Config.StartUrl})");

        var appUserModelId = MicaPaths.AppUserModelId(Config.Slug);

        // Before any window exists: this is what gives each app its own taskbar
        // button, pinning target and toast identity, even though every Mica app
        // shares one executable.
        AppIdentity.ApplyToProcess(appUserModelId);
        AppIdentity.PublishDisplayInfo(appUserModelId, Config.Name, MicaPaths.IconFile(Config.Slug));

        if (!string.IsNullOrEmpty(Config.Slug)
            && SingleInstanceHelper.RedirectIfAlreadyRunning($"Mica.{Config.Slug}"))
        {
            StartupLog.Write("Redirected to the running instance");

            // Application.Exit() before a window exists faults inside Microsoft.UI.Xaml;
            // this instance has nothing to tear down, so leave immediately.
            Environment.Exit(0);
            return;
        }

        Window = new MainWindow();

        // The taskbar reads the id from the window, and creates the button the
        // moment it is shown — so this has to land between construction and
        // Activate, or the app shows up as its executable name.
        AppIdentity.ApplyToWindow(WindowHandle, appUserModelId);

        Window.Activate();
        StartupLog.Write("Window activated");
    }

    /// <summary>
    /// Resolves which app to run. <c>--app &lt;slug&gt;</c> is what shortcuts use;
    /// <c>--url &lt;url&gt;</c> builds a throwaway config, which the builder uses to
    /// preview a site before the user commits to creating the app.
    /// </summary>
    private static WebAppConfig ResolveConfig(string[] argv)
    {
        for (var i = 1; i < argv.Length - 1; i++)
        {
            switch (argv[i])
            {
                case "--app":
                    return WebAppConfig.Load(MicaPaths.ConfigFile(argv[i + 1]));

                case "--url":
                    var uri = new Uri(argv[i + 1]);
                    return new WebAppConfig
                    {
                        Name = uri.Host,
                        StartUrl = uri.ToString(),
                        AllowedHosts = [uri.Host],
                    };
            }
        }

        // Started without arguments — a dock or a pinned item launching the
        // executable directly. Each app's executable is named after its slug, so
        // its own file name says which app to run.
        var ownName = Path.GetFileNameWithoutExtension(Environment.ProcessPath ?? string.Empty);
        var config = Path.Combine(MicaPaths.AppFolder(ownName), "app.json");
        if (ownName.Length > 0 && File.Exists(config))
        {
            return WebAppConfig.Load(config);
        }

        throw new ArgumentException(
            "Mica.Host expects --app <slug> or --url <url>. Launch apps from their shortcut.");
    }
}
