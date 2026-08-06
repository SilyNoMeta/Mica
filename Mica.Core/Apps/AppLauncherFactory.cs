using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Mica.Core.Integration;

namespace Mica.Core.Apps;

/// <summary>
/// Gives each generated app its own executable, carrying the site's icon.
/// <para>
/// The AppUserModelID on the window and the shortcut is what Windows' own taskbar
/// reads, but plenty of shells — third-party docks especially — identify a running
/// app by its executable instead, and show the icon compiled into it. Sharing one
/// <c>Mica.Host.exe</c> makes every app look identical to those. So each app gets a
/// copy of the host executable named after it, with its icon resource replaced.
/// </para>
/// <para>
/// A renamed copy works because a .NET apphost stores the path of its managed
/// assembly internally rather than deriving it from its own file name. WinUI does
/// not: it looks up its resource index as <c>&lt;executable name&gt;.pri</c>, and a
/// copy without one dies inside Microsoft.UI.Xaml before reaching any app code. So
/// the launcher is always a pair — the executable and its resource index. Everything
/// else, including the runtime, stays shared.
/// </para>
/// </summary>
[SupportedOSPlatform("windows")]
public static class AppLauncherFactory
{
    private const int RtIcon = 3;
    private const int RtGroupIcon = 14;
    private const int RtVersion = 16;
    private const ushort DefaultLanguage = 0x0409;

    private static string HostFolder => Path.GetDirectoryName(AppRegistry.HostExecutable)!;

    /// <summary>Path of the per-app executable, next to the shared host.</summary>
    public static string LauncherPath(string slug) => Path.Combine(HostFolder, $"{slug}.exe");

    private static string ResourceIndexPath(string slug) => Path.Combine(HostFolder, $"{slug}.pri");

    /// <summary>
    /// Creates or refreshes the executable for an app. Returns the shared host when
    /// the copy cannot be made — a read-only install directory, or the app running —
    /// so app creation degrades instead of failing.
    /// </summary>
    public static string Create(WebAppConfig config)
    {
        var slug = config.Slug;
        var launcher = LauncherPath(slug);

        try
        {
            var hostIndex = Path.ChangeExtension(AppRegistry.HostExecutable, ".pri");
            if (!File.Exists(hostIndex))
            {
                throw new FileNotFoundException("The host resource index is missing.", hostIndex);
            }

            File.Copy(AppRegistry.HostExecutable, launcher, overwrite: true);
            File.Copy(hostIndex, ResourceIndexPath(slug), overwrite: true);

            PatchResources(launcher, config);

            return launcher;
        }
        catch (Exception ex)
        {
            StartupLog.WriteException($"Launcher.{slug}", ex);
            return File.Exists(launcher) ? launcher : AppRegistry.HostExecutable;
        }
    }

    public static void Delete(string slug)
    {
        foreach (var path in new[] { LauncherPath(slug), ResourceIndexPath(slug) })
        {
            try
            {
                if (File.Exists(path))
                {
                    File.Delete(path);
                }
            }
            catch (Exception ex)
            {
                StartupLog.WriteException($"Launcher.Delete.{slug}", ex);
            }
        }
    }

    // ---- Icon resources ------------------------------------------------------

    /// <summary>
    /// Stamps the app's identity onto its executable: the site's icon, and a version
    /// resource naming it after the app. Both are rewritten in one update so the file
    /// is never left half-branded.
    /// </summary>
    private static void PatchResources(string executablePath, WebAppConfig config)
    {
        var iconPath = MicaPaths.IconFile(config.Slug);
        var icon = File.Exists(iconPath) ? IconFile.Read(iconPath) : null;

        // Reuse the ids and language the host already carries, so the shell keeps
        // resolving the same resources rather than finding two candidates.
        var groupIcon = FindResource(executablePath, RtGroupIcon);
        var version = FindResource(executablePath, RtVersion);

        var handle = BeginUpdateResource(executablePath, bDeleteExistingResources: false);
        if (handle == IntPtr.Zero)
        {
            throw new InvalidOperationException(
                $"Cannot open {Path.GetFileName(executablePath)} for resource update.");
        }

        var committed = false;
        try
        {
            if (icon is not null)
            {
                for (var i = 0; i < icon.Images.Count; i++)
                {
                    Write(handle, RtIcon, (IntPtr)(i + 1), groupIcon.Language, icon.Images[i]);
                }

                Write(handle, RtGroupIcon, (IntPtr)groupIcon.Id, groupIcon.Language, icon.BuildGroupDirectory());
            }

            Write(
                handle,
                RtVersion,
                (IntPtr)version.Id,
                version.Language,
                VersionResource.Build(config.Name, $"{config.Slug}.exe", config.StartUri.Host));

            if (!EndUpdateResource(handle, fDiscard: false))
            {
                throw new InvalidOperationException("Committing the resource update failed.");
            }

            committed = true;
        }
        finally
        {
            if (!committed)
            {
                EndUpdateResource(handle, fDiscard: true);
            }
        }
    }

    private static void Write(IntPtr handle, int type, IntPtr name, ushort language, byte[] data)
    {
        var buffer = Marshal.AllocHGlobal(data.Length);
        try
        {
            Marshal.Copy(data, 0, buffer, data.Length);
            if (!UpdateResource(handle, (IntPtr)type, name, language, buffer, (uint)data.Length))
            {
                throw new InvalidOperationException($"UpdateResource failed for type {type}.");
            }
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }

    /// <summary>Where a resource of the given type already lives in the executable.</summary>
    private readonly record struct ResourceSlot(int Id, ushort Language);

    private static ResourceSlot FindResource(string executablePath, int type)
    {
        var module = LoadLibraryEx(executablePath, IntPtr.Zero, LoadLibraryAsDataFile);
        if (module == IntPtr.Zero)
        {
            return new ResourceSlot(1, DefaultLanguage);
        }

        var id = 0;
        ushort language = 0;

        try
        {
            EnumResourceNames(module, (IntPtr)type, (_, _, name, _) =>
            {
                // Integer resource names arrive as a value below 0x10000; named
                // resources are string pointers and are left alone.
                if (id != 0 || name.ToInt64() is <= 0 or >= 0x10000)
                {
                    return true;
                }

                id = (int)name.ToInt64();
                EnumResourceLanguages(module, (IntPtr)type, name, (_, _, _, lang, _) =>
                {
                    language = lang;
                    return false;
                }, IntPtr.Zero);

                return false;
            }, IntPtr.Zero);
        }
        catch
        {
            // Fall through to the defaults below.
        }
        finally
        {
            FreeLibrary(module);
        }

        return new ResourceSlot(id == 0 ? 1 : id, language);
    }

    private const uint LoadLibraryAsDataFile = 0x00000002;

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr BeginUpdateResource(string fileName, [MarshalAs(UnmanagedType.Bool)] bool bDeleteExistingResources);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool UpdateResource(IntPtr update, IntPtr type, IntPtr name, ushort language, IntPtr data, uint size);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool EndUpdateResource(IntPtr update, [MarshalAs(UnmanagedType.Bool)] bool fDiscard);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr LoadLibraryEx(string fileName, IntPtr file, uint flags);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool FreeLibrary(IntPtr module);

    private delegate bool EnumResourceNamesProc(IntPtr module, IntPtr type, IntPtr name, IntPtr param);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool EnumResourceNames(IntPtr module, IntPtr type, EnumResourceNamesProc callback, IntPtr param);

    private delegate bool EnumResourceLanguagesProc(IntPtr module, IntPtr type, IntPtr name, ushort language, IntPtr param);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool EnumResourceLanguages(IntPtr module, IntPtr type, IntPtr name, EnumResourceLanguagesProc callback, IntPtr param);

    /// <summary>
    /// A parsed .ico file. On disk an icon is a directory of images; inside an
    /// executable the same thing is split into one RT_ICON per image plus an
    /// RT_GROUP_ICON index that references them by resource id.
    /// </summary>
    private sealed class IconFile
    {
        private readonly List<Entry> _entries = [];

        public List<byte[]> Images { get; } = [];

        public static IconFile Read(string path)
        {
            var bytes = File.ReadAllBytes(path);
            var icon = new IconFile();
            var count = BitConverter.ToUInt16(bytes, 4);

            for (var i = 0; i < count; i++)
            {
                var offset = 6 + (16 * i);
                var length = (int)BitConverter.ToUInt32(bytes, offset + 8);
                var imageOffset = (int)BitConverter.ToUInt32(bytes, offset + 12);

                icon._entries.Add(new Entry(
                    bytes[offset],
                    bytes[offset + 1],
                    bytes[offset + 2],
                    BitConverter.ToUInt16(bytes, offset + 4),
                    BitConverter.ToUInt16(bytes, offset + 6),
                    (uint)length));

                icon.Images.Add(bytes[imageOffset..(imageOffset + length)]);
            }

            return icon;
        }

        public byte[] BuildGroupDirectory()
        {
            using var stream = new MemoryStream();
            using var writer = new BinaryWriter(stream);

            writer.Write((ushort)0);
            writer.Write((ushort)1);
            writer.Write((ushort)_entries.Count);

            for (var i = 0; i < _entries.Count; i++)
            {
                var entry = _entries[i];
                writer.Write(entry.Width);
                writer.Write(entry.Height);
                writer.Write(entry.Colors);
                writer.Write((byte)0);
                writer.Write(entry.Planes);
                writer.Write(entry.BitCount);
                writer.Write(entry.Length);
                writer.Write((ushort)(i + 1));
            }

            writer.Flush();
            return stream.ToArray();
        }

        private sealed record Entry(
            byte Width,
            byte Height,
            byte Colors,
            ushort Planes,
            ushort BitCount,
            uint Length);
    }
}
