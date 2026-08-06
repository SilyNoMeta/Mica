using System.Text.RegularExpressions;
using Microsoft.UI.Xaml;
using Microsoft.Windows.AppLifecycle;
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

        // Double-clicking an app pack has to reach the Mica already open rather than
        // starting a second one — two windows managing the same apps would show each
        // other's work as stale. The running instance gets the path through Activated.
        if (SingleInstanceHelper.RedirectIfAlreadyRunning(
                $"{MicaPaths.ProductName}.Builder", OnRedirectedActivation))
        {
            StartupLog.Write("Redirected to the running Mica");
            Environment.Exit(0);
            return;
        }

        var appUserModelId = $"{MicaPaths.ProductName}.Builder";
        AppIdentity.ApplyToProcess(appUserModelId);

        // Put Mica in the Start menu on every run: an unpackaged app cannot be found
        // by Start or search until a shortcut exists, and installing Mica is exactly
        // what the user did to get here.
        AppRegistry.EnsureBuilderShortcut();

        // Same reasoning for the file type, plus one of its own: the association
        // holds an absolute path, and it carries a name Explorer shows, which is
        // written here because here is where Mica knows the user's language.
        FileAssociation.Register(AppRegistry.BuilderExecutable, Strings.Get("PackFileType"));

        // Shortcuts hold an absolute path to the host, so a moved or rebuilt
        // installation would leave every app pointing at a path that is gone.
        AppRegistry.RepairAll();

        Window = new MainWindow(PackArgument(argv.Skip(1)));
        AppIdentity.ApplyToWindow(WindowHandle, appUserModelId);
        Window.Activate();
    }

    // ---- Packs handed over by the shell ---------------------------------------

    /// <summary>
    /// A later launch was redirected here. Runs off the UI thread, so everything it
    /// touches goes through the dispatcher.
    /// </summary>
    private void OnRedirectedActivation(object? sender, AppActivationArguments args)
    {
        if (PackFromActivation(args) is not { } path)
        {
            return;
        }

        Window?.DispatcherQueue.TryEnqueue(() =>
        {
            // The user double-clicked a file; the window they are being asked about
            // it in should be in front of them.
            Window.Activate();

            if (Window is MainWindow main)
            {
                main.OfferImport(path);
            }
        });
    }

    private static string? PackFromActivation(AppActivationArguments args) =>
        args.Data is Windows.ApplicationModel.Activation.ILaunchActivatedEventArgs launch
            ? PackFromCommandLine(launch.Arguments)
            : null;

    /// <summary>
    /// The pack named on a redirected activation. Unpackaged apps receive the raw
    /// command line rather than a parsed list, so the file has to be picked back out
    /// of it — quoted, as the shell passes it, or bare if someone typed it.
    /// </summary>
    private static string? PackFromCommandLine(string commandLine) =>
        PackArgument(PackReferenceRegex()
            .Matches(commandLine)
            .Select(match => match.Groups["path"].Value));

    private static string? PackArgument(IEnumerable<string> candidates) =>
        candidates.FirstOrDefault(candidate =>
            candidate.EndsWith(AppPack.FileExtension, StringComparison.OrdinalIgnoreCase)
            && File.Exists(candidate));

    [GeneratedRegex("\"(?<path>[^\"]+\\.mica)\"|(?<path>\\S+\\.mica)", RegexOptions.IgnoreCase)]
    private static partial Regex PackReferenceRegex();

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
