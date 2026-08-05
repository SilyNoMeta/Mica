using System.Runtime.InteropServices;
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

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern int SetCurrentProcessExplicitAppUserModelID(string appId);

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
            Exit();
            return;
        }

        if (!string.IsNullOrEmpty(Config.Slug))
        {
            StartupLog.Initialize(MicaPaths.LogFile(Config.Slug));
        }

        StartupLog.Write($"Launching {Config.Name} ({Config.StartUrl})");

        // Must happen before the first window exists: this is what gives each app
        // its own taskbar button, pinning target and toast identity, even though
        // every Mica app shares one executable.
        try
        {
            SetCurrentProcessExplicitAppUserModelID(MicaPaths.AppUserModelId(Config.Slug));
        }
        catch (Exception ex)
        {
            StartupLog.WriteException("App.SetAumid", ex);
        }

        if (!string.IsNullOrEmpty(Config.Slug)
            && SingleInstanceHelper.RedirectIfAlreadyRunning($"Mica.{Config.Slug}"))
        {
            StartupLog.Write("Redirected to the running instance");
            Exit();
            return;
        }

        Window = new MainWindow();
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

        throw new ArgumentException(
            "Mica.Host expects --app <slug> or --url <url>. Launch apps from their shortcut.");
    }
}
