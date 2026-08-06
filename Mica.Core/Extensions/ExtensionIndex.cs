using System.Text.Json;

namespace Mica.Core.Extensions;

/// <summary>
/// Remembers which extensions Mica installed, and where their folders are.
/// <para>
/// This exists because an unpacked extension's id is not the one it has in the
/// store: Chromium derives it from the folder path, so the id WebView2 reports back
/// matches neither the store id nor the folder Mica unpacked it into. Without a
/// record, an installed extension cannot be found again — not to show its icon, not
/// to read its manifest, not to remove it.
/// </para>
/// </summary>
public sealed class ExtensionIndex
{
    /// <param name="RuntimeId">The id WebView2 reports, derived from the folder path.</param>
    /// <param name="StoreId">The Chrome Web Store id, which is what a store page names.</param>
    /// <param name="Folder">Where the unpacked extension lives.</param>
    public sealed record Entry(string RuntimeId, string StoreId, string Folder);

    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true };

    private readonly string _path;
    private List<Entry> _entries;

    private ExtensionIndex(string path, List<Entry> entries)
    {
        _path = path;
        _entries = entries;
    }

    public static ExtensionIndex Load(string extensionsFolder)
    {
        var path = Path.Combine(extensionsFolder, "installed.json");

        try
        {
            if (File.Exists(path))
            {
                var entries = JsonSerializer.Deserialize<List<Entry>>(File.ReadAllText(path), Options);
                return new ExtensionIndex(path, entries ?? []);
            }
        }
        catch
        {
            // A damaged index costs the toolbar buttons, not the extensions.
        }

        return new ExtensionIndex(path, []);
    }

    public IReadOnlyList<Entry> Entries => _entries;

    public Entry? ByRuntimeId(string runtimeId) =>
        _entries.FirstOrDefault(e => string.Equals(e.RuntimeId, runtimeId, StringComparison.OrdinalIgnoreCase));

    public bool HasStoreId(string storeId) =>
        _entries.Any(e => string.Equals(e.StoreId, storeId, StringComparison.OrdinalIgnoreCase));

    public void Record(string runtimeId, string storeId, string folder)
    {
        _entries = [.. _entries.Where(e =>
            !string.Equals(e.RuntimeId, runtimeId, StringComparison.OrdinalIgnoreCase)
            && !string.Equals(e.StoreId, storeId, StringComparison.OrdinalIgnoreCase))];

        _entries.Add(new Entry(runtimeId, storeId, folder));
        Save();
    }

    public void Remove(string runtimeId)
    {
        _entries = [.. _entries.Where(e =>
            !string.Equals(e.RuntimeId, runtimeId, StringComparison.OrdinalIgnoreCase))];

        Save();
    }

    private void Save()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
            File.WriteAllText(_path, JsonSerializer.Serialize(_entries, Options));
        }
        catch
        {
            // Losing the record is recoverable on the next install.
        }
    }
}
