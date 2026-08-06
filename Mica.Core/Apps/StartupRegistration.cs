using System.Runtime.Versioning;
using Microsoft.Win32;
using Mica.Core.Integration;

namespace Mica.Core.Apps;

/// <summary>
/// Starts an app with Windows, through the per-user Run key.
/// <para>
/// Windows lists whatever it finds here under Settings › Apps › Startup, where the
/// user can disable it. That switch is theirs: Windows records the decision beside
/// the entry and Mica does not remove or rewrite the entry to fight it.
/// </para>
/// </summary>
[SupportedOSPlatform("windows")]
public static class StartupRegistration
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";

    /// <summary>The name Windows shows in its startup list.</summary>
    private static string ValueName(WebAppConfig config) => $"Mica - {config.Name}";

    public static bool IsEnabled(WebAppConfig config)
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKey);
            return key?.GetValue(ValueName(config)) is not null;
        }
        catch (Exception ex)
        {
            StartupLog.WriteException("Startup.Read", ex);
            return false;
        }
    }

    public static void Set(WebAppConfig config, bool enabled)
    {
        try
        {
            using var key = Registry.CurrentUser.CreateSubKey(RunKey);

            if (!enabled)
            {
                key.DeleteValue(ValueName(config), throwOnMissingValue: false);
                return;
            }

            // --tray so a machine that has just booted does not open a window the
            // user did not ask for; the app waits in the notification area.
            var launcher = AppLauncherFactory.LauncherPath(config.Slug);
            var target = File.Exists(launcher) ? launcher : AppRegistry.HostExecutable;

            key.SetValue(ValueName(config), $"\"{target}\" --app {config.Slug} --tray");
        }
        catch (Exception ex)
        {
            StartupLog.WriteException("Startup.Write", ex);
        }
    }

    /// <summary>Removes the entry when an app is deleted.</summary>
    public static void Remove(WebAppConfig config)
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKey, writable: true);
            key?.DeleteValue(ValueName(config), throwOnMissingValue: false);
        }
        catch (Exception ex)
        {
            StartupLog.WriteException("Startup.Remove", ex);
        }
    }
}
