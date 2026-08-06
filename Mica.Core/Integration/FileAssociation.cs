using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Microsoft.Win32;
using Mica.Core.Apps;

namespace Mica.Core.Integration;

/// <summary>
/// Makes a <c>.mica</c> app pack open in Mica when double-clicked.
/// <para>
/// Registered under <c>HKCU\Software\Classes</c>, which needs no elevation and
/// affects only this user — the same bargain as the rest of Mica. Written on every
/// run rather than once at install: the association points at an absolute path, so
/// a moved or reinstalled Mica would otherwise leave Explorer opening a file with
/// an executable that is no longer there.
/// </para>
/// <para>
/// The type name Explorer shows comes from Mica's own string catalogue, so it is
/// in the user's language. Doing that from an installer would mean a resource
/// indirection (<c>@path,-id</c>) and a message table; doing it here costs a
/// registry write we already make.
/// </para>
/// </summary>
[SupportedOSPlatform("windows")]
public static class FileAssociation
{
    /// <summary>The ProgID under which the pack type is registered.</summary>
    public const string ProgId = "Mica.AppPack";

    private const string ClassesRoot = @"Software\Classes";

    /// <summary>
    /// Points <c>.mica</c> at <paramref name="executable"/>. Never throws: an app
    /// that cannot claim a file type still works, the file just does not open by
    /// itself.
    /// </summary>
    /// <param name="executable">Full path to Mica.Builder.exe.</param>
    /// <param name="typeName">What Explorer calls the file, in the user's language.</param>
    public static void Register(string executable, string typeName)
    {
        try
        {
            using (var progId = Registry.CurrentUser.CreateSubKey($@"{ClassesRoot}\{ProgId}"))
            {
                progId.SetValue(null, typeName);

                using (var icon = progId.CreateSubKey("DefaultIcon"))
                {
                    icon.SetValue(null, $"\"{executable}\",0");
                }

                using var shell = progId.CreateSubKey(@"shell\open");

                // Named so the Open with… list says Mica rather than Mica.Builder.
                shell.SetValue("FriendlyAppName", MicaPaths.ProductName);

                using var command = shell.CreateSubKey("command");
                command.SetValue(null, $"\"{executable}\" \"%1\"");
            }

            using (var extension = Registry.CurrentUser.CreateSubKey($@"{ClassesRoot}\{AppPack.FileExtension}"))
            {
                extension.SetValue(null, ProgId);

                // Lets Explorer offer Mica in Open with… even when the user has
                // pointed .mica somewhere else — a claim, not a seizure.
                using var progIds = extension.CreateSubKey("OpenWithProgids");
                progIds.SetValue(ProgId, Array.Empty<byte>(), RegistryValueKind.None);
            }

            // Without this the icon and the type name only appear once Explorer
            // next restarts.
            SHChangeNotify(SHCNE_ASSOCCHANGED, SHCNF_IDLIST, IntPtr.Zero, IntPtr.Zero);
        }
        catch (Exception ex)
        {
            StartupLog.WriteException("FileAssociation.Register", ex);
        }
    }

    /// <summary>Gives the file type back, on uninstall.</summary>
    public static void Remove()
    {
        try
        {
            Registry.CurrentUser.DeleteSubKeyTree($@"{ClassesRoot}\{ProgId}", throwOnMissingSubKey: false);
            Registry.CurrentUser.DeleteSubKeyTree($@"{ClassesRoot}\{AppPack.FileExtension}", throwOnMissingSubKey: false);

            SHChangeNotify(SHCNE_ASSOCCHANGED, SHCNF_IDLIST, IntPtr.Zero, IntPtr.Zero);
        }
        catch (Exception ex)
        {
            StartupLog.WriteException("FileAssociation.Remove", ex);
        }
    }

    private const int SHCNE_ASSOCCHANGED = 0x08000000;
    private const uint SHCNF_IDLIST = 0x0000;

    [DllImport("shell32.dll")]
    private static extern void SHChangeNotify(int eventId, uint flags, IntPtr item1, IntPtr item2);
}
