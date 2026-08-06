using System.IO.Compression;
using System.Net.Http;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Mica.Core.Extensions;

/// <summary>
/// Installs a Chrome Web Store extension into an app's browser profile.
/// <para>
/// WebView2 only accepts extensions as an unpacked folder, and the Web Store's
/// own "Add to Chrome" button does nothing inside an embedded browser. So the
/// user browses the store in the app, hands us the extension's link, and this
/// fetches the packed CRX from Google's update service, strips the CRX header —
/// the rest of the file is an ordinary ZIP — and unpacks it where the profile
/// can load it.
/// </para>
/// </summary>
public static partial class CrxInstaller
{
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromMinutes(2) };

    /// <summary>
    /// Pulls the 32-character extension id out of a store URL, or accepts a bare id.
    /// Returns null when the input is not an extension reference at all.
    /// </summary>
    public static string? ExtractId(string input)
    {
        input = input.Trim();

        if (input.Length == 32 && BareIdRegex().IsMatch(input))
        {
            return input.ToLowerInvariant();
        }

        var fromStore = StoreUrlRegex().Match(input);
        if (fromStore.Success)
        {
            return fromStore.Groups["id"].Value.ToLowerInvariant();
        }

        // The update service's own URL, which is what the store's "Add to Chrome"
        // button ends up requesting inside an embedded browser.
        var fromUpdate = UpdateUrlRegex().Match(input);
        return fromUpdate.Success ? fromUpdate.Groups["id"].Value.ToLowerInvariant() : null;
    }

    /// <summary>
    /// Downloads and unpacks an extension, returning the folder to hand to
    /// <c>CoreWebView2Profile.AddBrowserExtensionAsync</c>.
    /// </summary>
    /// <param name="id">The Chrome Web Store extension id.</param>
    /// <param name="extensionsFolder">This app's extensions folder.</param>
    /// <param name="browserVersion">
    /// The running WebView2 version. The update service serves the build matching
    /// the browser that asks, so passing the real version avoids being handed an
    /// extension the runtime cannot load.
    /// </param>
    public static async Task<string> InstallAsync(
        string id,
        string extensionsFolder,
        string browserVersion,
        CancellationToken cancellationToken = default)
    {
        // An extension can be tens of megabytes; it streams to a temporary file
        // rather than sitting in memory for the whole install.
        var download = await DownloadAsync(id, browserVersion, cancellationToken);

        try
        {
            var target = Path.Combine(extensionsFolder, id);
            if (Directory.Exists(target))
            {
                Directory.Delete(target, recursive: true);
            }

            Directory.CreateDirectory(target);

            using (var file = File.OpenRead(download))
            using (var zip = new SubStream(file, FindZipOffset(file)))
            using (var archive = new ZipArchive(zip, ZipArchiveMode.Read))
            {
                archive.ExtractToDirectory(target, overwriteFiles: true);
            }

            if (!File.Exists(Path.Combine(target, "manifest.json")))
            {
                Directory.Delete(target, recursive: true);
                throw new InvalidDataException("The downloaded package does not contain a browser extension.");
            }

            return target;
        }
        finally
        {
            try
            {
                File.Delete(download);
            }
            catch
            {
                // A leftover temporary file costs a few megabytes, not the install.
            }
        }
    }

    /// <summary>Reads an unpacked extension's display name, resolving localised names.</summary>
    public static string ReadName(string folder, string fallback)
    {
        try
        {
            using var document = JsonDocument.Parse(File.ReadAllText(Path.Combine(folder, "manifest.json")));
            var root = document.RootElement;

            if (!root.TryGetProperty("name", out var nameElement) || nameElement.GetString() is not { } name)
            {
                return fallback;
            }

            if (!name.StartsWith("__MSG_", StringComparison.Ordinal))
            {
                return name;
            }

            var key = name[6..].TrimEnd('_');
            var locale = root.TryGetProperty("default_locale", out var l) ? l.GetString() ?? "en" : "en";
            var messages = Path.Combine(folder, "_locales", locale, "messages.json");
            if (!File.Exists(messages))
            {
                return fallback;
            }

            using var localeDocument = JsonDocument.Parse(File.ReadAllText(messages));
            foreach (var entry in localeDocument.RootElement.EnumerateObject())
            {
                if (entry.Name.Equals(key, StringComparison.OrdinalIgnoreCase)
                    && entry.Value.TryGetProperty("message", out var message))
                {
                    return message.GetString() ?? fallback;
                }
            }

            return fallback;
        }
        catch
        {
            return fallback;
        }
    }

    /// <summary>Downloads the packed extension to a temporary file and returns its path.</summary>
    private static async Task<string> DownloadAsync(string id, string browserVersion, CancellationToken cancellationToken)
    {
        var major = browserVersion.Split('.').FirstOrDefault() ?? "140";
        var url =
            "https://clients2.google.com/service/update2/crx" +
            "?response=redirect&acceptformat=crx2,crx3" +
            $"&prodversion={major}.0.0.0" +
            $"&x=id%3D{id}%26installsource%3Dondemand%26uc";

        var download = Path.GetTempFileName();

        try
        {
            using var response = await Http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                throw new HttpRequestException(
                    $"The Chrome Web Store did not return this extension ({(int)response.StatusCode}).");
            }

            await using var file = new FileStream(
                download, FileMode.Create, FileAccess.Write, FileShare.None, bufferSize: 81_920, useAsync: true);
            await response.Content.CopyToAsync(file, cancellationToken);

            if (file.Length < 16)
            {
                throw new InvalidDataException("The Chrome Web Store returned an empty package.");
            }

            return download;
        }
        catch
        {
            try
            {
                File.Delete(download);
            }
            catch
            {
                // Best effort on the way to an error the caller reports.
            }

            throw;
        }
    }

    /// <summary>
    /// Locates the ZIP payload inside a CRX container. CRX2 and CRX3 differ only
    /// in how the signature block before the archive is sized.
    /// </summary>
    private static int FindZipOffset(Stream crx)
    {
        var header = new byte[16];
        var total = 0;

        while (total < header.Length)
        {
            var read = crx.Read(header, total, header.Length - total);
            if (read == 0)
            {
                throw new InvalidDataException("Not a CRX package.");
            }

            total += read;
        }

        if (header[0] != 'C' || header[1] != 'r' || header[2] != '2' || header[3] != '4')
        {
            // Some mirrors serve the bare ZIP.
            return header[0] == 'P' && header[1] == 'K' ? 0 : throw new InvalidDataException("Not a CRX package.");
        }

        var version = BitConverter.ToUInt32(header, 4);

        return version switch
        {
            3 => 12 + (int)BitConverter.ToUInt32(header, 8),
            2 => 16 + (int)BitConverter.ToUInt32(header, 8) + (int)BitConverter.ToUInt32(header, 12),
            _ => throw new InvalidDataException($"Unsupported CRX version {version}."),
        };
    }

    /// <summary>
    /// A read-only window onto another stream. ZipArchive wants the archive to
    /// fill its stream, and the CRX payload starts after the signature block; this
    /// presents exactly that without copying the payload anywhere.
    /// </summary>
    private sealed class SubStream(Stream inner, long offset) : Stream
    {
        public override bool CanRead => true;
        public override bool CanSeek => inner.CanSeek;
        public override bool CanWrite => false;
        public override long Length => inner.Length - offset;

        public override long Position
        {
            get => inner.Position - offset;
            set => inner.Position = value + offset;
        }

        public override void Flush()
        {
        }

        public override int Read(byte[] buffer, int readOffset, int count) =>
            inner.Read(buffer, readOffset, count);

        public override long Seek(long seekOffset, SeekOrigin origin) =>
            inner.Seek(seekOffset + (origin == SeekOrigin.Begin ? offset : 0), origin) - offset;

        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int writeOffset, int count) => throw new NotSupportedException();
    }

    [GeneratedRegex("^[a-p]{32}$", RegexOptions.IgnoreCase)]
    private static partial Regex BareIdRegex();

    [GeneratedRegex(@"/detail/(?:[^/]+/)?(?<id>[a-p]{32})", RegexOptions.IgnoreCase)]
    private static partial Regex StoreUrlRegex();

    [GeneratedRegex(@"id(?:=|%3D)(?<id>[a-p]{32})", RegexOptions.IgnoreCase)]
    private static partial Regex UpdateUrlRegex();
}
