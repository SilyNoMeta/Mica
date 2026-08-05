using System.Runtime.InteropServices;
using System.Text;
using Mica.Core.Integration;

namespace Mica.Core.Apps;

/// <summary>
/// Writes Start menu shortcuts that carry an explicit AppUserModelID.
/// <para>
/// This is half of what makes one shared host executable behave like many separate
/// apps: Windows resolves a taskbar button's name and icon by matching its
/// AppUserModelID against Start menu shortcuts. The other half is
/// <see cref="AppIdentity"/>, which puts the same id on the window.
/// </para>
/// </summary>
public static class ShortcutFactory
{
    public static void Create(
        string shortcutPath,
        string targetPath,
        string arguments,
        string iconPath,
        string description,
        string appUserModelId)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(shortcutPath)!);

        var link = (IShellLinkW)new ShellLink();
        link.SetPath(targetPath);
        link.SetArguments(arguments);
        link.SetWorkingDirectory(Path.GetDirectoryName(targetPath)!);
        link.SetDescription(Cap(description, 250));

        if (File.Exists(iconPath))
        {
            link.SetIconLocation(iconPath, 0);
        }

        ShellProperties.SetString((IPropertyStore)link, PropertyKey.AppUserModelId, appUserModelId);

        ((IPersistFile)link).Save(shortcutPath, true);
        Marshal.FinalReleaseComObject(link);
    }

    public static void Delete(string shortcutPath)
    {
        if (File.Exists(shortcutPath))
        {
            File.Delete(shortcutPath);
        }
    }

    private static string Cap(string value, int max) =>
        string.IsNullOrEmpty(value) || value.Length <= max ? value : value[..max];

    [ComImport]
    [Guid("00021401-0000-0000-C000-000000000046")]
    private class ShellLink
    {
    }

    [ComImport]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    [Guid("000214F9-0000-0000-C000-000000000046")]
    private interface IShellLinkW
    {
        void GetPath([Out][MarshalAs(UnmanagedType.LPWStr)] StringBuilder file, int maxChars, IntPtr findData, uint flags);

        void GetIDList(out IntPtr idList);

        void SetIDList(IntPtr idList);

        void GetDescription([Out][MarshalAs(UnmanagedType.LPWStr)] StringBuilder name, int maxChars);

        void SetDescription([MarshalAs(UnmanagedType.LPWStr)] string name);

        void GetWorkingDirectory([Out][MarshalAs(UnmanagedType.LPWStr)] StringBuilder directory, int maxChars);

        void SetWorkingDirectory([MarshalAs(UnmanagedType.LPWStr)] string directory);

        void GetArguments([Out][MarshalAs(UnmanagedType.LPWStr)] StringBuilder arguments, int maxChars);

        void SetArguments([MarshalAs(UnmanagedType.LPWStr)] string arguments);

        void GetHotkey(out short hotkey);

        void SetHotkey(short hotkey);

        void GetShowCmd(out int showCmd);

        void SetShowCmd(int showCmd);

        void GetIconLocation([Out][MarshalAs(UnmanagedType.LPWStr)] StringBuilder iconPath, int maxChars, out int iconIndex);

        void SetIconLocation([MarshalAs(UnmanagedType.LPWStr)] string iconPath, int iconIndex);

        void SetRelativePath([MarshalAs(UnmanagedType.LPWStr)] string relativePath, uint reserved);

        void Resolve(IntPtr window, uint flags);

        void SetPath([MarshalAs(UnmanagedType.LPWStr)] string file);
    }

    [ComImport]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    [Guid("0000010B-0000-0000-C000-000000000046")]
    private interface IPersistFile
    {
        void GetClassID(out Guid classId);

        [PreserveSig]
        int IsDirty();

        void Load([MarshalAs(UnmanagedType.LPWStr)] string fileName, uint mode);

        void Save([MarshalAs(UnmanagedType.LPWStr)] string? fileName, [MarshalAs(UnmanagedType.Bool)] bool remember);

        void SaveCompleted([MarshalAs(UnmanagedType.LPWStr)] string fileName);

        void GetCurFile([Out][MarshalAs(UnmanagedType.LPWStr)] StringBuilder fileName);
    }
}
