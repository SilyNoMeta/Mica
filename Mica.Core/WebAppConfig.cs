using System.Text.Json;
using System.Text.Json.Serialization;

namespace Mica.Core;

/// <summary>
/// Everything that distinguishes one generated app from another. Serialized to
/// <c>app.json</c> inside the app folder; the host reads it at startup, so a
/// single host executable can serve every app the user creates.
/// </summary>
public sealed class WebAppConfig
{
    /// <summary>Folder-safe identifier, unique per app (e.g. "gemini").</summary>
    public string Slug { get; set; } = string.Empty;

    /// <summary>Display name shown in the title bar, Start menu and taskbar.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>The page loaded on startup.</summary>
    public string StartUrl { get; set; } = string.Empty;

    /// <summary>
    /// Hosts that may be navigated inside the app, subdomains included. Anything
    /// else opens in the system browser.
    /// </summary>
    public List<string> AllowedHosts { get; set; } = [];

    public int WindowWidth { get; set; } = 1280;

    public int WindowHeight { get; set; } = 860;

    /// <summary>Page zoom, persisted from the settings pane.</summary>
    public double Zoom { get; set; } = 1.0;

    /// <summary>True when the user pinned the toolbar so it stays visible.</summary>
    public bool ToolbarPinned { get; set; }

    /// <summary>
    /// Closing the window leaves the app running in the notification area instead of
    /// quitting. This is what makes web notifications keep arriving: the page stays
    /// loaded, so the site can still raise them.
    /// <para>
    /// Off by default — a background process nobody asked for, holding a browser
    /// engine in memory, is not a reasonable default.
    /// </para>
    /// </summary>
    public bool RunInBackground { get; set; }

    /// <summary>True when the app starts with Windows, minimized to the notification area.</summary>
    public bool StartWithWindows { get; set; }

    /// <summary>
    /// Gives this app a browser engine of its own instead of sharing one with the
    /// other apps.
    /// <para>
    /// Sharing is the default because the engine is what costs: apps that share one
    /// pay for a single browser, GPU and network process between them — measured at
    /// roughly 280 MB saved per app. Sessions are separate either way; what an app
    /// gains by being alone is that a hung or crashed engine no longer takes the
    /// others down with it.
    /// </para>
    /// </summary>
    public bool DedicatedEngine { get; set; }

    public DateTimeOffset Created { get; set; } = DateTimeOffset.Now;

    [JsonIgnore]
    public Uri StartUri => new(StartUrl);

    /// <summary>
    /// True when <paramref name="host"/> — or one of its parent domains — may be
    /// navigated inside the app.
    /// </summary>
    public bool IsAllowedHost(string host)
    {
        if (string.IsNullOrEmpty(host))
        {
            return false;
        }

        var candidate = host.ToLowerInvariant();
        while (true)
        {
            foreach (var allowed in AllowedHosts)
            {
                if (string.Equals(allowed, candidate, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            var dot = candidate.IndexOf('.');
            if (dot < 0 || dot == candidate.Length - 1)
            {
                return false;
            }

            candidate = candidate[(dot + 1)..];
        }
    }

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    public static WebAppConfig Load(string path) =>
        JsonSerializer.Deserialize<WebAppConfig>(File.ReadAllText(path), SerializerOptions)
        ?? throw new InvalidDataException($"Empty app configuration: {path}");

    public void Save(string path) =>
        File.WriteAllText(path, JsonSerializer.Serialize(this, SerializerOptions));
}
