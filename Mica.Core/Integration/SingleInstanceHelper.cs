using Microsoft.Windows.AppLifecycle;

namespace Mica.Core.Integration;

/// <summary>
/// Keeps one window per generated app. Because every Mica app runs from the same
/// executable, the instance key is the app slug — two different apps launched at
/// once are two instances, but launching the same app twice reactivates the
/// existing window.
/// </summary>
public static class SingleInstanceHelper
{
    /// <summary>
    /// True when another instance already owns this app and activation was
    /// redirected to it — the caller should exit immediately.
    /// </summary>
    public static bool RedirectIfAlreadyRunning(string key)
    {
        try
        {
            var instance = AppInstance.FindOrRegisterForKey(key);
            if (instance.IsCurrent)
            {
                return false;
            }

            var args = AppInstance.GetCurrent().GetActivatedEventArgs();
            instance.RedirectActivationToAsync(args).AsTask().GetAwaiter().GetResult();
            return true;
        }
        catch (Exception ex)
        {
            // A failure here must not stop the app from opening at all.
            StartupLog.WriteException("SingleInstance", ex);
            return false;
        }
    }
}
