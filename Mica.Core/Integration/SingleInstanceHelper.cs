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
    /// <param name="key">What counts as "the same app" — a slug, or a product name.</param>
    /// <param name="onActivated">
    /// Raised on the instance that owns the key, each time a later launch is
    /// redirected to it. This is how a second launch carrying a file gets that file
    /// to the window already open. Fires on a background thread.
    /// </param>
    public static bool RedirectIfAlreadyRunning(
        string key,
        EventHandler<AppActivationArguments>? onActivated = null)
    {
        try
        {
            var instance = AppInstance.FindOrRegisterForKey(key);
            if (instance.IsCurrent)
            {
                if (onActivated is not null)
                {
                    instance.Activated += onActivated;
                }

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
