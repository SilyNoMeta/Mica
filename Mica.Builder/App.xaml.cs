using System.Runtime.InteropServices;
using Microsoft.UI.Xaml;
using Mica.Core;
using Mica.Core.Apps;

namespace Mica.Builder;

/// <summary>
/// The Mica window itself: where the user sees their apps and creates new ones.
/// </summary>
public partial class App : Application
{
    public static Window Window { get; private set; } = null!;

    public static nint WindowHandle => WinRT.Interop.WindowNative.GetWindowHandle(Window);

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern int SetCurrentProcessExplicitAppUserModelID(string appId);

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

        try
        {
            SetCurrentProcessExplicitAppUserModelID($"{MicaPaths.ProductName}.Builder");
        }
        catch
        {
            // Cosmetic only.
        }

        Window = new MainWindow();
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
