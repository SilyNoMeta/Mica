using System.Diagnostics;
using System.Runtime.Versioning;
using System.Text;
using Microsoft.Win32;
using Mica.Core.Integration;
using Mica.Core.Sites;

namespace Mica.Core.Apps;

/// <summary>
/// Creates, lists and removes the apps the user has built. An app is a folder
/// under %LOCALAPPDATA%\Mica\apps holding its configuration, icon and browser
/// profile, plus a Start menu shortcut that launches the shared host with that
/// app's slug. Nothing is installed system-wide and nothing needs elevation.
/// </summary>
[SupportedOSPlatform("windows")]
public static class AppRegistry
{
    private const string UninstallKeyRoot = @"Software\Microsoft\Windows\CurrentVersion\Uninstall";

    /// <summary>Every app currently installed, newest last.</summary>
    public static List<WebAppConfig> List()
    {
        var apps = new List<WebAppConfig>();
        if (!Directory.Exists(MicaPaths.AppsRoot))
        {
            return apps;
        }

        foreach (var folder in Directory.EnumerateDirectories(MicaPaths.AppsRoot))
        {
            var config = Path.Combine(folder, "app.json");
            if (!File.Exists(config))
            {
                continue;
            }

            try
            {
                apps.Add(WebAppConfig.Load(config));
            }
            catch
            {
                // A corrupt app folder should not hide the healthy ones.
            }
        }

        return [.. apps.OrderBy(a => a.Created)];
    }

    public static WebAppConfig? Find(string slug)
    {
        var path = MicaPaths.ConfigFile(slug);
        return File.Exists(path) ? WebAppConfig.Load(path) : null;
    }

    /// <summary>
    /// Builds an app for <paramref name="url"/>: reads the site's own name and
    /// icon, writes the app folder, and registers the Start menu shortcut.
    /// </summary>
    public static async Task<WebAppConfig> CreateAsync(
        Uri url,
        string? nameOverride = null,
        IEnumerable<string>? extraHosts = null,
        CancellationToken cancellationToken = default)
    {
        var metadata = await SiteInspector.InspectAsync(url, cancellationToken);
        var name = string.IsNullOrWhiteSpace(nameOverride) ? metadata.Name : nameOverride.Trim();
        var slug = UniqueSlug(name, metadata.ResolvedUrl);

        MicaPaths.EnsureAppFolder(slug);

        // Host rules cover both the address the user gave and wherever it landed:
        // a signed-out visit often redirects somewhere the signed-in one never goes.
        var hosts = HostRules.ForUrl(url);
        hosts.AddRange(HostRules.ForUrl(metadata.ResolvedUrl));

        if (extraHosts is not null)
        {
            hosts.AddRange(extraHosts
                .Select(h => h.Trim().ToLowerInvariant())
                .Where(h => h.Length > 0));
        }

        hosts = [.. hosts.Distinct(StringComparer.OrdinalIgnoreCase)];

        var config = new WebAppConfig
        {
            Slug = slug,
            Name = name,

            // The address the user asked for, not where a signed-out fetch was sent:
            // gemini.google.com/app redirects to the marketing page until you sign in,
            // and baking that in would make the app open the wrong page forever.
            StartUrl = url.ToString(),
            AllowedHosts = hosts,
        };

        try
        {
            await IconPipeline.BuildAsync(
                metadata.IconUrl,
                name,
                MicaPaths.IconFile(slug),
                Path.Combine(MicaPaths.AppFolder(slug), "icon.png"),
                cancellationToken);

            config.Save(MicaPaths.ConfigFile(slug));

            CreateShortcut(config);
            WriteUninstallEntry(config);
            AppIdentity.PublishDisplayInfo(
                MicaPaths.AppUserModelId(slug),
                config.Name,
                MicaPaths.IconFile(slug));
        }
        catch
        {
            // A half-created app would sit in the list without a working shortcut,
            // and would push the next attempt onto a "-2" slug. Undo instead.
            TryDiscard(config);
            throw;
        }

        return config;
    }

    /// <summary>Re-reads the site's icon and name for an existing app.</summary>
    public static async Task RefreshAsync(WebAppConfig config, CancellationToken cancellationToken = default)
    {
        var metadata = await SiteInspector.InspectAsync(config.StartUri, cancellationToken);

        await IconPipeline.BuildAsync(
            metadata.IconUrl,
            config.Name,
            MicaPaths.IconFile(config.Slug),
            Path.Combine(MicaPaths.AppFolder(config.Slug), "icon.png"),
            cancellationToken);

        CreateShortcut(config);
        WriteUninstallEntry(config);
    }

    /// <summary>
    /// Removes an app completely: shortcut, uninstall entry, configuration,
    /// browser profile and any extension it had installed.
    /// </summary>
    public static void Remove(WebAppConfig config)
    {
        ShortcutFactory.Delete(ShortcutPath(config));
        AppIdentity.Remove(MicaPaths.AppUserModelId(config.Slug));
        AppLauncherFactory.Delete(config.Slug);
        StartupRegistration.Remove(config);

        try
        {
            Registry.CurrentUser.DeleteSubKeyTree($@"{UninstallKeyRoot}\Mica.{config.Slug}", throwOnMissingSubKey: false);
        }
        catch
        {
            // The app is going away regardless of what the registry says.
        }

        var folder = MicaPaths.AppFolder(config.Slug);
        if (Directory.Exists(folder))
        {
            Directory.Delete(folder, recursive: true);
        }

        if (Directory.Exists(MicaPaths.StartMenuFolder)
            && !Directory.EnumerateFileSystemEntries(MicaPaths.StartMenuFolder).Any())
        {
            Directory.Delete(MicaPaths.StartMenuFolder);
        }
    }

    private static void TryDiscard(WebAppConfig config)
    {
        try
        {
            Remove(config);
        }
        catch
        {
            // The original failure is the one worth reporting.
        }
    }

    /// <summary>
    /// Removes every generated app and Mica's own traces outside the install folder:
    /// the Start menu group, the shell identity registrations and the data directory.
    /// Used by the uninstaller, which asks the user first — this deletes the browser
    /// profiles, so it signs them out of every app they built.
    /// </summary>
    public static void RemoveAll()
    {
        foreach (var config in List())
        {
            try
            {
                Remove(config);
            }
            catch (Exception ex)
            {
                StartupLog.WriteException($"RemoveAll.{config.Slug}", ex);
            }
        }

        try
        {
            ShortcutFactory.Delete(MicaPaths.ShortcutFile(MicaPaths.ProductName));
            AppIdentity.Remove($"{MicaPaths.ProductName}.Builder");

            if (Directory.Exists(MicaPaths.StartMenuFolder)
                && !Directory.EnumerateFileSystemEntries(MicaPaths.StartMenuFolder).Any())
            {
                Directory.Delete(MicaPaths.StartMenuFolder);
            }

            if (Directory.Exists(MicaPaths.Root))
            {
                Directory.Delete(MicaPaths.Root, recursive: true);
            }
        }
        catch (Exception ex)
        {
            StartupLog.WriteException("RemoveAll.Product", ex);
        }
    }

    public static void Launch(WebAppConfig config)
    {
        var launcher = AppLauncherFactory.LauncherPath(config.Slug);

        Process.Start(new ProcessStartInfo(File.Exists(launcher) ? launcher : HostExecutable)
        {
            UseShellExecute = false,
            Arguments = $"--app {config.Slug}",
        });
    }

    // ---- Shortcut and uninstall registration ---------------------------------

    public static string ShortcutPath(WebAppConfig config) => MicaPaths.ShortcutFile(config.Name);

    private static void CreateShortcut(WebAppConfig config)
    {
        // Shells that identify apps by executable rather than by AppUserModelID get
        // a real per-app executable to look at; the argument stays for the shared-host
        // fallback and because being explicit costs nothing.
        var launcher = AppLauncherFactory.Create(config);

        ShortcutFactory.Create(
            ShortcutPath(config),
            launcher,
            $"--app {config.Slug}",
            MicaPaths.IconFile(config.Slug),
            $"{config.Name} — {config.StartUri.Host}",
            MicaPaths.AppUserModelId(config.Slug));
    }

    /// <summary>
    /// Publishes the app in Settings › Installed apps, so removing it feels like
    /// removing any other Windows app rather than deleting a folder.
    /// </summary>
    private static void WriteUninstallEntry(WebAppConfig config)
    {
        try
        {
            using var key = Registry.CurrentUser.CreateSubKey($@"{UninstallKeyRoot}\Mica.{config.Slug}");
            key.SetValue("DisplayName", config.Name);
            key.SetValue("DisplayIcon", MicaPaths.IconFile(config.Slug));
            key.SetValue("Publisher", $"{MicaPaths.ProductName} ({config.StartUri.Host})");
            key.SetValue("InstallLocation", MicaPaths.AppFolder(config.Slug));
            key.SetValue("UninstallString", $"\"{BuilderExecutable}\" --uninstall {config.Slug}");
            key.SetValue("NoModify", 1, RegistryValueKind.DWord);
            key.SetValue("NoRepair", 1, RegistryValueKind.DWord);
        }
        catch
        {
            // Cosmetic: the app still works and the builder can still remove it.
        }
    }

    /// <summary>
    /// Makes sure Mica itself is in the Start menu, next to the apps it builds.
    /// Nothing else puts it there — an unpackaged app is invisible to Start and to
    /// search until a shortcut exists — and the path is refreshed on every run so a
    /// moved or rebuilt installation keeps working.
    /// </summary>
    public static void EnsureBuilderShortcut()
    {
        try
        {
            var shortcut = MicaPaths.ShortcutFile(MicaPaths.ProductName);
            var icon = Path.Combine(AppContext.BaseDirectory, "Assets", "AppIcon.ico");
            var appUserModelId = $"{MicaPaths.ProductName}.Builder";

            ShortcutFactory.Create(
                shortcut,
                BuilderExecutable,
                string.Empty,
                icon,
                "Build a Windows app from any website",
                appUserModelId);

            AppIdentity.PublishDisplayInfo(appUserModelId, MicaPaths.ProductName, icon);
            StartupLog.Write($"Builder shortcut ensured at {shortcut} -> {BuilderExecutable}");
        }
        catch (Exception ex)
        {
            StartupLog.WriteException("Builder.Shortcut", ex);
        }
    }

    /// <summary>
    /// Re-points every app's shortcut, uninstall entry and shell identity at the
    /// current installation. Shortcuts store an absolute path to the host, so moving
    /// or reinstalling Mica would otherwise leave every app launching from a path
    /// that no longer exists.
    /// </summary>
    public static void RepairAll()
    {
        foreach (var config in List())
        {
            try
            {
                CreateShortcut(config);
                WriteUninstallEntry(config);
                AppIdentity.PublishDisplayInfo(
                    MicaPaths.AppUserModelId(config.Slug),
                    config.Name,
                    MicaPaths.IconFile(config.Slug));
            }
            catch (Exception ex)
            {
                StartupLog.WriteException($"Repair.{config.Slug}", ex);
            }
        }
    }

    // ---- Executable discovery ------------------------------------------------

    /// <summary>
    /// The shared host executable. Installed side by side with the builder; when
    /// running from the build output, the sibling project's output is used so the
    /// whole flow can be exercised without packaging anything first.
    /// </summary>
    public static string HostExecutable { get; } = ResolveExecutable("Mica.Host.exe");

    public static string BuilderExecutable { get; } = ResolveExecutable("Mica.Builder.exe");

    private static string ResolveExecutable(string fileName)
    {
        var local = Path.Combine(AppContext.BaseDirectory, fileName);
        if (File.Exists(local))
        {
            return local;
        }

        var developmentPath = FindInSiblingBuildOutput(fileName);
        return developmentPath ?? local;
    }

    private static string? FindInSiblingBuildOutput(string fileName)
    {
        var project = Path.GetFileNameWithoutExtension(fileName);
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory is not null)
        {
            var sibling = Path.Combine(directory.FullName, project, "bin");
            if (Directory.Exists(sibling))
            {
                var match = Directory
                    .EnumerateFiles(sibling, fileName, SearchOption.AllDirectories)
                    .OrderByDescending(File.GetLastWriteTimeUtc)
                    .FirstOrDefault();

                if (match is not null)
                {
                    return match;
                }
            }

            directory = directory.Parent;
        }

        return null;
    }

    // ---- Slugs ---------------------------------------------------------------

    private static string UniqueSlug(string name, Uri url)
    {
        var baseSlug = Slugify(name);
        if (baseSlug.Length == 0)
        {
            baseSlug = Slugify(url.Host);
        }

        if (baseSlug.Length == 0)
        {
            baseSlug = "app";
        }

        var slug = baseSlug;
        var suffix = 2;
        while (Directory.Exists(MicaPaths.AppFolder(slug)))
        {
            slug = $"{baseSlug}-{suffix++}";
        }

        return slug;
    }

    private static string Slugify(string value)
    {
        var builder = new StringBuilder(value.Length);
        var lastWasDash = true;

        foreach (var c in value.ToLowerInvariant())
        {
            if (char.IsAsciiLetterOrDigit(c))
            {
                builder.Append(c);
                lastWasDash = false;
            }
            else if (!lastWasDash && builder.Length > 0)
            {
                builder.Append('-');
                lastWasDash = true;
            }
        }

        return builder.ToString().Trim('-');
    }
}
