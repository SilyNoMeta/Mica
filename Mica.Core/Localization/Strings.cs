using System.Globalization;
using Microsoft.UI.Xaml;
using Microsoft.Windows.ApplicationModel.Resources;

namespace Mica.Core.Localization;

/// <summary>
/// Reads the running executable's localized strings.
/// <para>
/// The resources deliberately live in the executable projects rather than here: in
/// an unpackaged app a class library's own <c>.resw</c> is not reachable at runtime
/// — neither through <c>x:Uid</c> nor through any resource map — while library code
/// reads the executable's resources without trouble. So the shell, which lives in
/// this library, asks its host for its words.
/// </para>
/// </summary>
public static class Strings
{
    private const string ResourceMap = "Resources";

    private static ResourceManager? _manager;
    private static ResourceContext? _context;
    private static string? _languageOverride;

    /// <summary>
    /// Forces the UI language, overriding what Windows is set to. Reads
    /// <c>--lang &lt;tag&gt;</c> from the command line; a tag Mica does not ship falls
    /// back to English. Call before building any window.
    /// </summary>
    public static void ApplyLanguageOverride(string[] argv)
    {
        for (var i = 1; i < argv.Length - 1; i++)
        {
            if (argv[i] is "--lang" or "--language")
            {
                _languageOverride = argv[i + 1];
                _context = null;
                return;
            }
        }
    }

    /// <summary>
    /// The string for <paramref name="key"/>, or the key itself when it is missing —
    /// a visibly wrong label beats an exception in the middle of building the UI.
    /// </summary>
    public static string Get(string key)
    {
        try
        {
            var value = Manager.MainResourceMap
                .GetValue($"{ResourceMap}/{key}", Context)
                .ValueAsString;

            if (!string.IsNullOrEmpty(value))
            {
                return value;
            }
        }
        catch
        {
            // Falls through to the report below.
        }

        ReportMissing(key);
        return key;
    }

    /// <summary>
    /// Records a key that resolved to nothing, once each. A missing string shows as
    /// its own key rather than throwing, which is right for the user but means a
    /// mistake — a key that only exists in the other project's catalogue, say — can
    /// otherwise sit in the interface unnoticed.
    /// </summary>
    private static void ReportMissing(string key)
    {
        lock (Missing)
        {
            if (!Missing.Add(key))
            {
                return;
            }
        }

        Integration.StartupLog.Write($"MISSING STRING: {key}");
    }

    private static readonly HashSet<string> Missing = new(StringComparer.Ordinal);

    public static string Format(string key, params object?[] arguments) =>
        string.Format(CultureInfo.CurrentCulture, Get(key), arguments);

    /// <summary>
    /// The reading direction of the resolved language. Arabic resolves right to left;
    /// every other language Mica ships resolves left to right.
    /// </summary>
    public static FlowDirection FlowDirection =>
        Get("FlowDirection").Equals("RightToLeft", StringComparison.OrdinalIgnoreCase)
            ? FlowDirection.RightToLeft
            : FlowDirection.LeftToRight;

    /// <summary>True when the UI is mirrored, so direction-bearing glyphs must flip too.</summary>
    public static bool IsRightToLeft => FlowDirection == FlowDirection.RightToLeft;

    private static ResourceManager Manager => _manager ??= new ResourceManager();

    /// <summary>
    /// The lookup context. Language selection goes through the context's qualifier
    /// rather than <c>ApplicationLanguages.PrimaryLanguageOverride</c>, which the
    /// modern resource stack ignores.
    /// </summary>
    private static ResourceContext Context
    {
        get
        {
            if (_context is not null)
            {
                return _context;
            }

            _context = Manager.CreateResourceContext();

            if (!string.IsNullOrWhiteSpace(_languageOverride))
            {
                try
                {
                    _context.QualifierValues["Language"] = _languageOverride;
                }
                catch
                {
                    // An unusable tag just leaves the system language in place.
                }
            }

            return _context;
        }
    }
}
