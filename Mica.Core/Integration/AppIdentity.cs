using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Microsoft.Win32;

namespace Mica.Core.Integration;

/// <summary>
/// Gives a generated app its own identity in the shell, even though every Mica app
/// runs from the same executable.
/// <para>
/// Setting the identity on the process alone is not enough: the taskbar reads the
/// AppUserModelID from the <em>window</em>, and a window that reports none falls
/// back to the executable — which is why every app would otherwise show up as
/// "Mica.Host". The id must also be written before the window is first shown,
/// because the taskbar button is created at that moment.
/// </para>
/// </summary>
[SupportedOSPlatform("windows")]
public static class AppIdentity
{
    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern int SetCurrentProcessExplicitAppUserModelID(string appId);

    [DllImport("shell32.dll")]
    private static extern int SHGetPropertyStoreForWindow(
        IntPtr window,
        ref Guid interfaceId,
        [MarshalAs(UnmanagedType.Interface)] out IPropertyStore store);

    /// <summary>Sets the identity for the process. Call before creating any window.</summary>
    public static void ApplyToProcess(string appUserModelId)
    {
        try
        {
            SetCurrentProcessExplicitAppUserModelID(appUserModelId);
        }
        catch (Exception ex)
        {
            StartupLog.WriteException("Identity.Process", ex);
        }
    }

    /// <summary>Sets the identity for a window. Call after it is created, before it is shown.</summary>
    public static void ApplyToWindow(IntPtr window, string appUserModelId)
    {
        try
        {
            var interfaceId = typeof(IPropertyStore).GUID;
            Marshal.ThrowExceptionForHR(SHGetPropertyStoreForWindow(window, ref interfaceId, out var store));

            try
            {
                ShellProperties.SetString(store, PropertyKey.AppUserModelId, appUserModelId);
            }
            finally
            {
                Marshal.FinalReleaseComObject(store);
            }
        }
        catch (Exception ex)
        {
            StartupLog.WriteException("Identity.Window", ex);
        }
    }

    /// <summary>
    /// Publishes the name and icon the shell should show for an AppUserModelID.
    /// <para>
    /// This has to be asserted by us: the unpackaged notification registration fills
    /// these in by guessing from the executable, and since every Mica app shares one
    /// executable, every app inherits whichever app happened to register first.
    /// </para>
    /// </summary>
    public static void PublishDisplayInfo(string appUserModelId, string displayName, string iconPath)
    {
        try
        {
            using var key = Registry.CurrentUser.CreateSubKey($@"Software\Classes\AppUserModelId\{appUserModelId}");
            key.SetValue("DisplayName", displayName);

            if (File.Exists(iconPath))
            {
                key.SetValue("IconUri", iconPath);
            }
        }
        catch (Exception ex)
        {
            StartupLog.WriteException("Identity.Publish", ex);
        }
    }

    /// <summary>Removes an app's identity registration.</summary>
    public static void Remove(string appUserModelId)
    {
        try
        {
            Registry.CurrentUser.DeleteSubKeyTree(
                $@"Software\Classes\AppUserModelId\{appUserModelId}",
                throwOnMissingSubKey: false);
        }
        catch
        {
            // The app is going away regardless of what the registry says.
        }
    }
}
