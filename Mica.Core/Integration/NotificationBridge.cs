using Microsoft.Web.WebView2.Core;
using Microsoft.Windows.AppNotifications;
using Microsoft.Windows.AppNotifications.Builder;

namespace Mica.Core.Integration;

/// <summary>
/// Turns the site's web notifications into real Windows toasts and grants the
/// web Notification permission automatically. Toasts are attributed to the app's
/// own AppUserModelID, so they show its name and icon in the Action Center.
/// </summary>
public sealed class NotificationBridge
{
    private const int MaxTitleLength = 120;
    private const int MaxBodyLength = 300;

    private bool _registered;

    /// <summary>
    /// Registers the notification channel. Unpackaged apps must do this
    /// explicitly; failure is non-fatal and simply disables toasts.
    /// </summary>
    public void Register()
    {
        try
        {
            AppNotificationManager.Default.Register();
            _registered = true;
        }
        catch (Exception ex)
        {
            StartupLog.WriteException("Notifications.Register", ex);
        }
    }

    public void Unregister()
    {
        if (!_registered)
        {
            return;
        }

        try
        {
            AppNotificationManager.Default.Unregister();
        }
        catch
        {
            // Best effort on shutdown.
        }
    }

    public void Attach(CoreWebView2 webView)
    {
        webView.PermissionRequested += OnPermissionRequested;
        webView.NotificationReceived += OnNotificationReceived;
    }

    private static void OnPermissionRequested(object? sender, CoreWebView2PermissionRequestedEventArgs e)
    {
        if (e.PermissionKind == CoreWebView2PermissionKind.Notifications)
        {
            e.State = CoreWebView2PermissionState.Allow;
        }
    }

    private void OnNotificationReceived(object? sender, CoreWebView2NotificationReceivedEventArgs e)
    {
        if (!_registered)
        {
            return;
        }

        try
        {
            var builder = new AppNotificationBuilder()
                .AddText(Cap(e.Notification.Title, MaxTitleLength))
                .AddText(Cap(e.Notification.Body, MaxBodyLength));

            AppNotificationManager.Default.Show(builder.BuildNotification());
            e.Handled = true;
        }
        catch (Exception ex)
        {
            // Let the page fall back to its own in-page notification.
            StartupLog.WriteException("Notifications.Show", ex);
        }
    }

    private static string Cap(string value, int maxLength) =>
        string.IsNullOrEmpty(value) || value.Length <= maxLength ? value : value[..maxLength];
}
