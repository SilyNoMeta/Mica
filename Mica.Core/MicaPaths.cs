namespace Mica.Core;

/// <summary>
/// Where Mica keeps its data. Everything lives under the user's local app data,
/// so creating and removing an app never needs elevation.
/// </summary>
public static class MicaPaths
{
    /// <summary>Product name, also used for the Start menu folder.</summary>
    public const string ProductName = "Mica";

    /// <summary>%LOCALAPPDATA%\Mica</summary>
    public static string Root { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        ProductName);

    /// <summary>%LOCALAPPDATA%\Mica\apps — one subfolder per generated app.</summary>
    public static string AppsRoot { get; } = Path.Combine(Root, "apps");

    /// <summary>
    /// Scratch area for the builder's preview window, which runs the shell against
    /// a site before an app exists. Kept out of <see cref="AppsRoot"/> so a preview
    /// never shows up as an installed app.
    /// </summary>
    public static string PreviewFolder { get; } = Path.Combine(Root, "preview");

    public static string AppFolder(string slug) =>
        string.IsNullOrEmpty(slug) ? PreviewFolder : Path.Combine(AppsRoot, slug);

    public static string ConfigFile(string slug) => Path.Combine(AppFolder(slug), "app.json");

    public static string IconFile(string slug) => Path.Combine(AppFolder(slug), "icon.ico");

    /// <summary>The WebView2 user data folder: one profile per app, so sign-ins stay separate.</summary>
    public static string ProfileFolder(string slug) => Path.Combine(AppFolder(slug), "profile");

    /// <summary>Unpacked browser extensions installed into this app's profile.</summary>
    public static string ExtensionsFolder(string slug) => Path.Combine(AppFolder(slug), "extensions");

    public static string LogFile(string slug) => Path.Combine(AppFolder(slug), "startup.log");

    /// <summary>The per-app AppUserModelID: taskbar grouping, pinning and toast identity.</summary>
    public static string AppUserModelId(string slug) => $"{ProductName}.App.{slug}";

    /// <summary>%APPDATA%\Microsoft\Windows\Start Menu\Programs\Mica</summary>
    public static string StartMenuFolder { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "Microsoft", "Windows", "Start Menu", "Programs", ProductName);

    public static string ShortcutFile(string name) =>
        Path.Combine(StartMenuFolder, $"{name}.lnk");

    public static void EnsureAppFolder(string slug)
    {
        Directory.CreateDirectory(AppFolder(slug));
        Directory.CreateDirectory(ProfileFolder(slug));
        Directory.CreateDirectory(ExtensionsFolder(slug));
    }
}
