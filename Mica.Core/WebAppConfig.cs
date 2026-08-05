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
