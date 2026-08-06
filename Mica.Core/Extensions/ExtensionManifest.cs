using System.Text.Json;

namespace Mica.Core.Extensions;

/// <summary>
/// Reads what an unpacked extension says about its toolbar button.
/// <para>
/// WebView2 reports an extension's id, name and enabled state and nothing else — no
/// icon, no popup. Both are in the manifest sitting in the folder Mica unpacked, so
/// that is where the toolbar gets them.
/// </para>
/// </summary>
public static class ExtensionManifest
{
    /// <summary>
    /// The icon to draw and the page to open for an extension's toolbar button.
    /// Either may be null: an extension is free to declare no action at all.
    /// </summary>
    public static (string? IconPath, string? ActionPage) ReadAction(string folder)
    {
        try
        {
            var path = Path.Combine(folder, "manifest.json");
            if (!File.Exists(path))
            {
                return (null, null);
            }

            using var document = JsonDocument.Parse(File.ReadAllText(path));
            var root = document.RootElement;

            // "action" is Manifest V3; "browser_action" and "page_action" are V2.
            JsonElement action = default;
            var hasAction = root.TryGetProperty("action", out action)
                || root.TryGetProperty("browser_action", out action)
                || root.TryGetProperty("page_action", out action);

            string? icon = null;
            string? page = null;

            if (hasAction)
            {
                if (action.TryGetProperty("default_icon", out var actionIcon))
                {
                    icon = LargestIcon(actionIcon);
                }

                if (action.TryGetProperty("default_popup", out var popup))
                {
                    page = popup.GetString();
                }
            }

            // Fall back to the extension's general icons, then to its options page,
            // so a button still appears for extensions that declare neither.
            if (icon is null && root.TryGetProperty("icons", out var icons))
            {
                icon = LargestIcon(icons);
            }

            if (page is null)
            {
                page = root.TryGetProperty("options_page", out var options)
                    ? options.GetString()
                    : root.TryGetProperty("options_ui", out var ui)
                        && ui.TryGetProperty("page", out var uiPage)
                            ? uiPage.GetString()
                            : null;
            }

            var iconPath = icon is null ? null : Path.Combine(folder, icon.Replace('/', Path.DirectorySeparatorChar));
            return (File.Exists(iconPath) ? iconPath : null, page);
        }
        catch
        {
            // A malformed manifest costs the button, not the app.
            return (null, null);
        }
    }

    /// <summary>
    /// Picks the largest declared size. The value is either a path or a map keyed by
    /// pixel size; the biggest scales down cleanly for a 16 px button on any display.
    /// </summary>
    private static string? LargestIcon(JsonElement icon)
    {
        if (icon.ValueKind == JsonValueKind.String)
        {
            return icon.GetString();
        }

        if (icon.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        string? best = null;
        var bestSize = -1;

        foreach (var entry in icon.EnumerateObject())
        {
            if (entry.Value.ValueKind != JsonValueKind.String)
            {
                continue;
            }

            var size = int.TryParse(entry.Name, out var parsed) ? parsed : 0;
            if (size > bestSize)
            {
                bestSize = size;
                best = entry.Value.GetString();
            }
        }

        return best;
    }
}
