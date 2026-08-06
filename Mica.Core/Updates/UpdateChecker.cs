using System.Diagnostics;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Reflection;
using System.Text.Json;
using Mica.Core.Integration;

namespace Mica.Core.Updates;

/// <summary>A release newer than the one running.</summary>
/// <param name="Version">The release version, from its tag.</param>
/// <param name="DownloadUrl">The installer attached to that release.</param>
/// <param name="SizeBytes">Its size, so the user can be told what a click will fetch.</param>
/// <param name="Notes">The release notes, trimmed for display.</param>
public sealed record UpdateInfo(Version Version, string DownloadUrl, long SizeBytes, string Notes);

/// <summary>
/// Looks for a newer Mica on GitHub, fetches its installer and hands over to it.
/// <para>
/// Only the Mica window does this, never a generated app: one request a day rather
/// than one per open app, and an update notice belongs where the user manages their
/// apps rather than in the middle of their messages.
/// </para>
/// <para>
/// Nothing is downloaded or run without the user asking. The installer is not code
/// signed, so SmartScreen may question it — that is far better met as the result of
/// a deliberate click than as something that happened by itself.
/// </para>
/// </summary>
public static class UpdateChecker
{
    private const string ReleasesApi = "https://api.github.com/repos/SilyNoMeta/Mica/releases/latest";
    private static readonly TimeSpan CheckInterval = TimeSpan.FromHours(24);

    // Created on first use, not in a field initialiser: those run in declaration
    // order, so a client built here would read Current before it has been assigned
    // and take the whole type down with a NullReferenceException.
    private static HttpClient? _http;

    private static HttpClient Http => _http ??= CreateClient();

    private static HttpClient CreateClient()
    {
        var client = new HttpClient { Timeout = TimeSpan.FromMinutes(10) };
        client.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("Mica", Current.ToString()));
        client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        return client;
    }

    /// <summary>The version of the running build, as stamped by the project file.</summary>
    public static Version Current { get; } =
        Assembly.GetEntryAssembly()?.GetName().Version ?? new Version(0, 0, 0);

    private static string StatePath => Path.Combine(MicaPaths.Root, "update-check.json");

    /// <summary>
    /// The newer release, or null when there is none, when the last check was recent,
    /// or when GitHub cannot be reached. Never throws: a missing network is not a
    /// reason to interrupt someone opening their apps.
    /// </summary>
    public static async Task<UpdateInfo?> CheckAsync(bool force = false, CancellationToken cancellationToken = default)
    {
        try
        {
            if (!force && !DueForCheck())
            {
                return null;
            }

            using var response = await Http.GetAsync(ReleasesApi, cancellationToken);
            RecordCheck();

            if (!response.IsSuccessStatusCode)
            {
                return null;
            }

            using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken));
            var release = document.RootElement;

            if (release.TryGetProperty("draft", out var draft) && draft.GetBoolean())
            {
                return null;
            }

            if (!release.TryGetProperty("tag_name", out var tag)
                || !Version.TryParse((tag.GetString() ?? string.Empty).TrimStart('v', 'V'), out var version)
                || version <= Current)
            {
                return null;
            }

            if (!release.TryGetProperty("assets", out var assets))
            {
                return null;
            }

            foreach (var asset in assets.EnumerateArray())
            {
                var name = asset.TryGetProperty("name", out var n) ? n.GetString() ?? string.Empty : string.Empty;

                if (!name.StartsWith("MicaSetup", StringComparison.OrdinalIgnoreCase)
                    || !name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                var url = asset.TryGetProperty("browser_download_url", out var u) ? u.GetString() : null;
                if (url is null)
                {
                    continue;
                }

                var size = asset.TryGetProperty("size", out var s) ? s.GetInt64() : 0;
                var notes = release.TryGetProperty("body", out var b) ? b.GetString() ?? string.Empty : string.Empty;
                var language = System.Globalization.CultureInfo.CurrentUICulture.TwoLetterISOLanguageName;

                return new UpdateInfo(version, url, size, SelectNotes(notes, language));
            }

            return null;
        }
        catch (Exception ex)
        {
            StartupLog.WriteException("Update.Check", ex);
            return null;
        }
    }

    /// <summary>Downloads the installer to a temporary file and returns its path.</summary>
    public static async Task<string> DownloadAsync(UpdateInfo update, CancellationToken cancellationToken = default)
    {
        var folder = Path.Combine(Path.GetTempPath(), "mica-update");
        Directory.CreateDirectory(folder);

        var path = Path.Combine(folder, $"MicaSetup-{update.Version}.exe");
        var bytes = await Http.GetByteArrayAsync(update.DownloadUrl, cancellationToken);

        // A truncated download would fail as a corrupt installer with no explanation;
        // fail here instead, where the reason is obvious.
        if (update.SizeBytes > 0 && bytes.LongLength != update.SizeBytes)
        {
            throw new IOException(
                $"The download is incomplete ({bytes.LongLength} of {update.SizeBytes} bytes).");
        }

        await File.WriteAllBytesAsync(path, bytes, cancellationToken);
        return path;
    }

    /// <summary>
    /// Runs the installer and leaves. It replaces files this process is using, so
    /// Mica has to be gone before it gets there; the installer reopens it.
    /// </summary>
    public static void InstallAndExit(string installerPath)
    {
        Process.Start(new ProcessStartInfo(installerPath)
        {
            UseShellExecute = true,
            Arguments = "/SILENT /SUPPRESSMSGBOXES /NORESTART /relaunch=1",
        });

        Environment.Exit(0);
    }

    private static bool DueForCheck()
    {
        try
        {
            if (!File.Exists(StatePath))
            {
                return true;
            }

            using var document = JsonDocument.Parse(File.ReadAllText(StatePath));
            return !document.RootElement.TryGetProperty("LastCheck", out var last)
                || !DateTimeOffset.TryParse(last.GetString(), out var when)
                || DateTimeOffset.Now - when > CheckInterval;
        }
        catch
        {
            return true;
        }
    }

    private static void RecordCheck()
    {
        try
        {
            Directory.CreateDirectory(MicaPaths.Root);
            File.WriteAllText(
                StatePath,
                JsonSerializer.Serialize(new { LastCheck = DateTimeOffset.Now }));
        }
        catch
        {
            // Losing the timestamp only means checking again sooner than needed.
        }
    }

    /// <summary>
    /// Picks the reader's language out of the release notes.
    /// <para>
    /// A release body is the matching CHANGELOG section, which carries one
    /// <c>### &lt;language&gt;</c> block per language the project writes. English is the
    /// fallback: Mica's interface is translated into twelve languages, but its
    /// changelog is written by hand, so most readers get English notes under a
    /// translated heading rather than nothing at all.
    /// </para>
    /// </summary>
    // A notice, not the changelog itself: left unbounded it grows until it squeezes
    // the list of apps out of the window.
    private const int MaxEntries = 4;
    private const int MaxEntryLength = 150;

    private static string SelectNotes(string body, string language)
    {
        var blocks = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        List<string>? current = null;

        foreach (var raw in body.Replace("\r", string.Empty).Split('\n'))
        {
            var line = raw.Trim();

            if (line.StartsWith("###", StringComparison.Ordinal))
            {
                var tag = line.TrimStart('#').Trim();

                // First block per language wins. A body holding several versions
                // would otherwise run them all together as one list.
                if (blocks.ContainsKey(tag))
                {
                    current = null;
                    continue;
                }

                current = blocks[tag] = [];
                continue;
            }

            if (line.StartsWith("##", StringComparison.Ordinal))
            {
                current = null;
                continue;
            }

            current?.Add(line);
        }

        var wanted = blocks.TryGetValue(language, out var exact)
            ? exact
            : blocks.TryGetValue("en", out var english) ? english : null;

        var lines = wanted ?? [.. body.Replace("\r", string.Empty).Split('\n').Select(l => l.Trim())];
        return Reflow(lines);
    }

    /// <summary>
    /// Turns changelog markdown into something an InfoBar can show. The file is hard
    /// wrapped for reading in a diff, so its line breaks land mid-sentence when they
    /// are carried straight into a panel that wraps on its own.
    /// </summary>
    private static string Reflow(IEnumerable<string> lines)
    {
        var entries = new List<string>();

        foreach (var line in lines)
        {
            if (line.Length == 0 || line.StartsWith('#') || line.StartsWith("<!--"))
            {
                continue;
            }

            if (line.StartsWith("- ", StringComparison.Ordinal) || line.StartsWith("* ", StringComparison.Ordinal))
            {
                entries.Add("• " + line[2..].Trim());
            }
            else if (entries.Count > 0)
            {
                entries[^1] = $"{entries[^1]} {line}";
            }
            else
            {
                entries.Add(line);
            }
        }

        var shown = entries
            .Take(MaxEntries)
            .Select(entry => entry.Length > MaxEntryLength
                ? entry[..MaxEntryLength].TrimEnd() + "…"
                : entry)
            .ToList();

        if (entries.Count > MaxEntries)
        {
            shown.Add("…");
        }

        return string.Join(Environment.NewLine, shown).Trim();
    }
}
