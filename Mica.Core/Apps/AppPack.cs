using System.Text.Json;
using System.Text.Json.Serialization;
using Mica.Core.Extensions;

namespace Mica.Core.Apps;

/// <summary>
/// A set of apps written to one file, so a machine can be given the same apps as
/// another without rebuilding them by hand.
/// <para>
/// The file holds what defines an app — its address, its name, its host rules, its
/// settings — and the <em>identifiers</em> of the extensions it runs, not their
/// bytes. Both the icon and the extensions are fetched again on the receiving
/// machine, which keeps a pack of ten apps at a few kilobytes instead of the tens
/// of megabytes an unpacked extension weighs, and means an imported app arrives
/// current rather than frozen at the day it was exported.
/// </para>
/// <para>
/// What a pack deliberately does not carry is the session. Chromium encrypts its
/// cookies with a key sealed by DPAPI to one Windows account on one machine, so
/// copied cookies are undecryptable anywhere else — and making them travel would
/// mean decrypting someone's sign-ins and moving them in the clear, which is the
/// technique of a credential stealer and not of a settings transfer. An imported
/// app opens signed out; the sites ask for a sign-in on a new device regardless.
/// </para>
/// </summary>
public sealed class AppPack
{
    /// <summary>The extension a pack is saved under.</summary>
    public const string FileExtension = ".mica";

    /// <summary>
    /// Bumped when the shape changes incompatibly. A reader that meets a higher
    /// number says so rather than importing half of what the file describes.
    /// </summary>
    public const int CurrentFormat = 1;

    /// <summary>One app as it travels: its configuration, minus anything machine-bound.</summary>
    public sealed class Entry
    {
        /// <summary>The slug it had on the source machine, reused when it is free.</summary>
        public string Slug { get; set; } = string.Empty;

        public string Name { get; set; } = string.Empty;

        public string StartUrl { get; set; } = string.Empty;

        public List<string> AllowedHosts { get; set; } = [];

        public int WindowWidth { get; set; } = 1280;

        public int WindowHeight { get; set; } = 860;

        public double Zoom { get; set; } = 1.0;

        public bool ToolbarPinned { get; set; }

        public bool RunInBackground { get; set; }

        public bool StartWithWindows { get; set; }

        public bool DedicatedEngine { get; set; }

        /// <summary>Chrome Web Store ids, re-downloaded on import.</summary>
        public List<string> Extensions { get; set; } = [];

        [JsonIgnore]
        public Uri StartUri => new(StartUrl);
    }

    public int Format { get; set; } = CurrentFormat;

    /// <summary>Names the tool that wrote the file, for anyone who opens it in an editor.</summary>
    public string Product { get; set; } = MicaPaths.ProductName;

    public string CreatedBy { get; set; } = string.Empty;

    public DateTimeOffset Created { get; set; } = DateTimeOffset.Now;

    public List<Entry> Apps { get; set; } = [];

    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    /// <summary>
    /// Describes an installed app for export, reading the extensions it runs out of
    /// its index. Ids that do not look like store ids are dropped: the index also
    /// remembers folder names, and only a real id can be downloaded again.
    /// </summary>
    public static Entry Describe(WebAppConfig config)
    {
        var extensions = ExtensionIndex
            .Load(MicaPaths.ExtensionsFolder(config.Slug))
            .Entries
            .Select(entry => CrxInstaller.ExtractId(entry.StoreId))
            .OfType<string>()
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        return new Entry
        {
            Slug = config.Slug,
            Name = config.Name,
            StartUrl = config.StartUrl,
            AllowedHosts = [.. config.AllowedHosts],
            WindowWidth = config.WindowWidth,
            WindowHeight = config.WindowHeight,
            Zoom = config.Zoom,
            ToolbarPinned = config.ToolbarPinned,
            RunInBackground = config.RunInBackground,
            StartWithWindows = config.StartWithWindows,
            DedicatedEngine = config.DedicatedEngine,
            Extensions = extensions,
        };
    }

    public static AppPack Of(IEnumerable<WebAppConfig> configs, string createdBy) => new()
    {
        CreatedBy = createdBy,
        Apps = [.. configs.Select(Describe)],
    };

    public void Save(string path) =>
        File.WriteAllText(path, JsonSerializer.Serialize(this, Options));

    /// <summary>
    /// Reads a pack, rejecting anything that is not one. The messages are shown to
    /// the user, so they name what is wrong with the file rather than which
    /// deserializer gave up.
    /// </summary>
    public static AppPack Load(string path)
    {
        AppPack? pack;

        try
        {
            pack = JsonSerializer.Deserialize<AppPack>(File.ReadAllText(path), Options);
        }
        catch (JsonException)
        {
            throw new InvalidDataException("This file is not a Mica app pack.");
        }

        if (pack is null || pack.Apps is null)
        {
            throw new InvalidDataException("This file is not a Mica app pack.");
        }

        if (pack.Format > CurrentFormat)
        {
            throw new InvalidDataException(
                $"This pack was written by a newer Mica (format {pack.Format}). Update Mica to import it.");
        }

        // A pack is plain JSON and someone may well write one by hand to set up a
        // fleet of machines; entries that cannot become an app are dropped rather
        // than failing the whole import.
        pack.Apps = [.. pack.Apps.Where(IsUsable)];

        return pack;
    }

    private static bool IsUsable(Entry entry) =>
        !string.IsNullOrWhiteSpace(entry.StartUrl)
        && Uri.TryCreate(entry.StartUrl, UriKind.Absolute, out var uri)
        && uri.Scheme is "http" or "https";
}
