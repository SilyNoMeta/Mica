namespace Mica.Core.Sites;

/// <summary>
/// Works out which hosts a generated app should treat as "inside the app".
/// <para>
/// Only top-level navigations are checked, so this list does not need CDN or
/// API hosts — it needs the site itself plus the places a sign-in flow legitimately
/// lands on.
/// </para>
/// </summary>
public static class HostRules
{
    /// <summary>
    /// Second-level suffixes that are really public suffixes, so the registrable
    /// domain is three labels rather than two.
    /// </summary>
    private static readonly HashSet<string> MultiLabelSuffixes = new(StringComparer.OrdinalIgnoreCase)
    {
        "co.uk", "org.uk", "ac.uk", "gov.uk", "co.jp", "or.jp", "ne.jp", "com.au",
        "net.au", "org.au", "com.br", "com.cn", "com.mx", "co.in", "co.nz", "co.za",
        "com.tr", "com.sg", "com.hk", "com.tw", "co.kr",
    };

    /// <summary>
    /// Extra sign-in / companion domains for sites whose auth flow leaves the
    /// main domain. Keyed by registrable domain.
    /// </summary>
    private static readonly Dictionary<string, string[]> Companions = new(StringComparer.OrdinalIgnoreCase)
    {
        ["google.com"] = ["youtube.com", "googleusercontent.com"],
        ["youtube.com"] = ["google.com", "googleusercontent.com"],
        ["messenger.com"] = ["facebook.com", "fb.com"],
        ["facebook.com"] = ["messenger.com", "fb.com"],
        ["instagram.com"] = ["facebook.com"],
        ["whatsapp.com"] = ["facebook.com"],
        ["github.com"] = ["githubusercontent.com"],
        ["x.com"] = ["twitter.com"],
        ["twitter.com"] = ["x.com"],
        ["openai.com"] = ["chatgpt.com", "auth0.com"],
        ["chatgpt.com"] = ["openai.com", "auth0.com"],
        ["claude.ai"] = ["anthropic.com"],
        ["microsoft.com"] = ["live.com", "microsoftonline.com", "office.com"],
        ["office.com"] = ["live.com", "microsoftonline.com", "microsoft.com", "sharepoint.com"],
        ["outlook.com"] = ["live.com", "microsoftonline.com", "office.com"],
        ["linkedin.com"] = ["licdn.com"],
        ["reddit.com"] = ["redd.it"],
        ["slack.com"] = ["slack-edge.com"],
        ["discord.com"] = ["discordapp.com"],
        ["notion.so"] = ["notion.com"],
        ["notion.com"] = ["notion.so"],
        ["spotify.com"] = ["scdn.co"],
    };

    /// <summary>
    /// Hosts every app trusts, because Mica itself sends the user there: the
    /// Chrome Web Store, used by the extension installer.
    /// </summary>
    public static readonly string[] AlwaysAllowed =
    [
        "chromewebstore.google.com",
    ];

    /// <summary>The registrable domain of a host — "gemini.google.com" becomes "google.com".</summary>
    public static string RegistrableDomain(string host)
    {
        var labels = host.Split('.', StringSplitOptions.RemoveEmptyEntries);
        if (labels.Length <= 2)
        {
            return host;
        }

        var lastTwo = $"{labels[^2]}.{labels[^1]}";
        return MultiLabelSuffixes.Contains(lastTwo) && labels.Length >= 3
            ? $"{labels[^3]}.{lastTwo}"
            : lastTwo;
    }

    /// <summary>Builds the default in-app host list for a site.</summary>
    public static List<string> ForUrl(Uri url)
    {
        var host = url.Host.ToLowerInvariant();
        var domain = RegistrableDomain(host);

        var hosts = new List<string> { domain };
        if (!string.Equals(host, domain, StringComparison.OrdinalIgnoreCase))
        {
            hosts.Insert(0, host);
        }

        if (Companions.TryGetValue(domain, out var companions))
        {
            hosts.AddRange(companions);
        }

        hosts.AddRange(AlwaysAllowed);

        return [.. hosts.Distinct(StringComparer.OrdinalIgnoreCase)];
    }

    /// <summary>
    /// True when a URL looks like part of a sign-in flow. Such navigations stay
    /// in the app even on an unlisted host, otherwise logging in would bounce the
    /// user into their browser and strand the session there.
    /// </summary>
    public static bool LooksLikeSignIn(Uri uri)
    {
        var path = uri.AbsolutePath.ToLowerInvariant();
        return path.Contains("oauth")
            || path.Contains("login")
            || path.Contains("signin")
            || path.Contains("sign-in")
            || path.Contains("auth")
            || path.Contains("sso")
            || path.Contains("saml")
            || path.Contains("checkpoint");
    }
}
