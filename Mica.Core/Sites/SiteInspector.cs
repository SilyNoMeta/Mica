using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Mica.Core.Sites;

/// <summary>What a site says about itself: the name to use and the best icon it publishes.</summary>
/// <param name="Name">Display name for the generated app.</param>
/// <param name="IconUrl">Best available icon, or null when the site publishes none.</param>
/// <param name="ResolvedUrl">The URL after redirects — what the app should actually open.</param>
public sealed record SiteMetadata(string Name, Uri? IconUrl, Uri ResolvedUrl);

/// <summary>
/// Reads a site's own name and icon so a generated app looks like the real thing
/// instead of a redrawn approximation. Nothing is bundled: every asset comes from
/// the site the user chose to wrap.
/// </summary>
public static partial class SiteInspector
{
    private static readonly HttpClient Http = CreateClient();

    private static HttpClient CreateClient()
    {
        var handler = new HttpClientHandler
        {
            AutomaticDecompression = DecompressionMethods.All,
            AllowAutoRedirect = true,
        };

        var client = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(25) };

        // Several large sites (Meta's in particular) serve an error interstitial to
        // requests that do not look like a real browser navigation.
        var headers = client.DefaultRequestHeaders;
        headers.Add("User-Agent",
            "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/140.0.0.0 Safari/537.36 Edg/140.0.0.0");
        headers.Add("Accept",
            "text/html,application/xhtml+xml,application/xml;q=0.9,image/avif,image/webp,*/*;q=0.8");
        headers.Add("Accept-Language", "en-US,en;q=0.9");
        headers.Add("Sec-Fetch-Dest", "document");
        headers.Add("Sec-Fetch-Mode", "navigate");
        headers.Add("Sec-Fetch-Site", "none");
        headers.Add("Sec-Fetch-User", "?1");
        headers.Add("Upgrade-Insecure-Requests", "1");

        return client;
    }

    public static async Task<SiteMetadata> InspectAsync(Uri url, CancellationToken cancellationToken = default)
    {
        var resolved = url;
        string html;

        try
        {
            using var response = await Http.GetAsync(url, cancellationToken);
            resolved = response.RequestMessage?.RequestUri ?? url;
            html = await response.Content.ReadAsStringAsync(cancellationToken);
        }
        catch (Exception)
        {
            // An unreachable or hostile site still deserves a usable app.
            return new SiteMetadata(FallbackName(url), null, url);
        }

        var candidates = new List<IconCandidate>();
        var name = string.Empty;

        var manifestHref = FindLinkHref(html, "manifest");
        if (manifestHref is not null && Uri.TryCreate(resolved, manifestHref, out var manifestUrl))
        {
            var (manifestName, manifestIcons) = await ReadManifestAsync(manifestUrl, cancellationToken);
            name = manifestName;
            candidates.AddRange(manifestIcons);
        }

        candidates.AddRange(FindIconLinks(html, resolved));

        if (string.IsNullOrWhiteSpace(name))
        {
            name = ReadMetaContent(html, "og:site_name")
                ?? ReadMetaContent(html, "application-name")
                ?? ReadTitle(html)
                ?? FallbackName(resolved);
        }

        var best = candidates.OrderByDescending(c => c.Score).FirstOrDefault();
        var iconUrl = best?.Url ?? new Uri(resolved, "/favicon.ico");

        return new SiteMetadata(CleanName(name, resolved), iconUrl, resolved);
    }

    // ---- Web app manifest ----------------------------------------------------

    private static async Task<(string Name, List<IconCandidate> Icons)> ReadManifestAsync(
        Uri manifestUrl,
        CancellationToken cancellationToken)
    {
        var icons = new List<IconCandidate>();

        try
        {
            var json = await Http.GetStringAsync(manifestUrl, cancellationToken);
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;

            var name = string.Empty;
            if (root.TryGetProperty("short_name", out var shortName))
            {
                name = shortName.GetString() ?? string.Empty;
            }

            if (string.IsNullOrWhiteSpace(name) && root.TryGetProperty("name", out var fullName))
            {
                name = fullName.GetString() ?? string.Empty;
            }

            if (root.TryGetProperty("icons", out var iconArray) && iconArray.ValueKind == JsonValueKind.Array)
            {
                foreach (var icon in iconArray.EnumerateArray())
                {
                    if (!icon.TryGetProperty("src", out var src) || src.GetString() is not { } srcValue)
                    {
                        continue;
                    }

                    if (!Uri.TryCreate(manifestUrl, srcValue, out var iconUrl))
                    {
                        continue;
                    }

                    var sizes = icon.TryGetProperty("sizes", out var s) ? s.GetString() ?? string.Empty : string.Empty;
                    var purpose = icon.TryGetProperty("purpose", out var p) ? p.GetString() ?? string.Empty : string.Empty;
                    icons.Add(IconCandidate.Create(iconUrl, sizes, purpose));
                }
            }

            return (name, icons);
        }
        catch
        {
            return (string.Empty, icons);
        }
    }

    // ---- HTML scraping -------------------------------------------------------

    private static IEnumerable<IconCandidate> FindIconLinks(string html, Uri baseUrl)
    {
        foreach (Match match in LinkTagRegex().Matches(html))
        {
            var tag = match.Value;
            var rel = ReadAttribute(tag, "rel");
            if (rel is null)
            {
                continue;
            }

            var relations = rel.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            var isIcon = relations.Any(r =>
                r.Equals("icon", StringComparison.OrdinalIgnoreCase)
                || r.Equals("shortcut", StringComparison.OrdinalIgnoreCase)
                || r.Equals("apple-touch-icon", StringComparison.OrdinalIgnoreCase)
                || r.Equals("apple-touch-icon-precomposed", StringComparison.OrdinalIgnoreCase));

            if (!isIcon)
            {
                continue;
            }

            var href = ReadAttribute(tag, "href");
            if (href is null || !Uri.TryCreate(baseUrl, href, out var iconUrl))
            {
                continue;
            }

            yield return IconCandidate.Create(iconUrl, ReadAttribute(tag, "sizes") ?? string.Empty, string.Empty);
        }
    }

    private static string? FindLinkHref(string html, string relation)
    {
        foreach (Match match in LinkTagRegex().Matches(html))
        {
            var rel = ReadAttribute(match.Value, "rel");
            if (rel is not null && rel.Split(' ').Any(r => r.Equals(relation, StringComparison.OrdinalIgnoreCase)))
            {
                return ReadAttribute(match.Value, "href");
            }
        }

        return null;
    }

    private static string? ReadAttribute(string tag, string name)
    {
        var match = Regex.Match(
            tag,
            $"""\b{Regex.Escape(name)}\s*=\s*(?:"(?<v>[^"]*)"|'(?<v>[^']*)'|(?<v>[^\s>]+))""",
            RegexOptions.IgnoreCase);
        return match.Success ? WebUtility.HtmlDecode(match.Groups["v"].Value).Trim() : null;
    }

    private static string? ReadMetaContent(string html, string property)
    {
        var match = Regex.Match(
            html,
            $"""<meta[^>]+(?:property|name)\s*=\s*["']{Regex.Escape(property)}["'][^>]*>""",
            RegexOptions.IgnoreCase);
        return match.Success ? ReadAttribute(match.Value, "content") : null;
    }

    private static string? ReadTitle(string html)
    {
        var match = Regex.Match(html, "<title[^>]*>(?<t>.*?)</title>",
            RegexOptions.IgnoreCase | RegexOptions.Singleline);
        return match.Success ? WebUtility.HtmlDecode(match.Groups["t"].Value).Trim() : null;
    }

    /// <summary>Titles are often "Brand | tagline"; keep the brand only.</summary>
    private static string CleanName(string name, Uri url)
    {
        name = name.Trim();
        foreach (var separator in new[] { " | ", " – ", " — ", " - ", " · ", ": " })
        {
            var index = name.IndexOf(separator, StringComparison.Ordinal);
            if (index > 2)
            {
                name = name[..index].Trim();
            }
        }

        name = string.Concat(name.Where(c => !Path.GetInvalidFileNameChars().Contains(c))).Trim();

        if (name.Length is 0 or > 60)
        {
            name = FallbackName(url);
        }

        return name;
    }

    private static string FallbackName(Uri url)
    {
        var host = url.Host;
        if (host.StartsWith("www.", StringComparison.OrdinalIgnoreCase))
        {
            host = host[4..];
        }

        var label = host.Split('.').FirstOrDefault() ?? host;
        return label.Length > 0 ? char.ToUpperInvariant(label[0]) + label[1..] : host;
    }

    [GeneratedRegex("<link[^>]*>", RegexOptions.IgnoreCase)]
    private static partial Regex LinkTagRegex();

    /// <summary>An icon the site advertises, with a score used to pick the best one.</summary>
    private sealed record IconCandidate(Uri Url, int Score)
    {
        public static IconCandidate Create(Uri url, string sizes, string purpose)
        {
            var path = url.AbsolutePath.ToLowerInvariant();
            var isVector = path.EndsWith(".svg", StringComparison.Ordinal);

            // A vector scales to any tile size without artefacts, so it outranks
            // every raster except a very large one.
            var score = isVector ? 900 : LargestDimension(sizes);

            if (score == 0)
            {
                // No size declared: .ico files are usually tiny favicons.
                score = path.EndsWith(".ico", StringComparison.Ordinal) ? 32 : 64;
            }

            // Maskable icons are drawn with heavy padding for OS-side cropping;
            // used as-is they look shrunken, so prefer a plain icon when both exist.
            if (purpose.Contains("maskable", StringComparison.OrdinalIgnoreCase)
                && !purpose.Contains("any", StringComparison.OrdinalIgnoreCase))
            {
                score /= 2;
            }

            return new IconCandidate(url, score);
        }

        private static int LargestDimension(string sizes)
        {
            var largest = 0;
            foreach (var token in sizes.Split(' ', StringSplitOptions.RemoveEmptyEntries))
            {
                if (token.Equals("any", StringComparison.OrdinalIgnoreCase))
                {
                    return 900;
                }

                var x = token.IndexOf('x', StringComparison.OrdinalIgnoreCase);
                if (x > 0 && int.TryParse(token[..x], out var width))
                {
                    largest = Math.Max(largest, width);
                }
            }

            return largest;
        }
    }
}
