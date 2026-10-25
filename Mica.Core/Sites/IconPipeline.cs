using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace Mica.Core.Sites;

/// <summary>
/// Turns whatever icon a site publishes into the assets a Windows app needs:
/// a multi-resolution <c>.ico</c> for the window, taskbar and shortcut, plus a
/// 512 px PNG used as the preview in the builder.
/// </summary>
[SupportedOSPlatform("windows")]
public static class IconPipeline
{
    private const int SourceSize = 512;

    /// <summary>
    /// The headless window the icon is rendered in. Deliberately larger than
    /// <see cref="SourceSize"/> — see the wrapper page in
    /// <see cref="TryRenderInBrowserAsync"/> for why the icon needs room around it.
    /// </summary>
    private const int ViewportSize = 1024;

    private static readonly int[] IcoSizes = [16, 24, 32, 48, 64, 128, 256];

    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(25) };

    /// <summary>
    /// Produces <paramref name="icoPath"/> and <paramref name="pngPath"/> for a site.
    /// Never throws for a missing or undecodable icon: it falls back to a lettermark
    /// so creating an app always succeeds.
    /// </summary>
    public static async Task BuildAsync(
        Uri? iconUrl,
        string appName,
        string icoPath,
        string pngPath,
        CancellationToken cancellationToken = default)
    {
        using var source = await LoadSourceAsync(iconUrl, appName, cancellationToken);
        using var trimmed = Trim(source);

        Directory.CreateDirectory(Path.GetDirectoryName(icoPath)!);

        using (var preview = Compose(trimmed, SourceSize, SourceSize, 1.0f))
        {
            preview.Save(pngPath, ImageFormat.Png);
        }

        WriteIco(trimmed, icoPath);
    }

    // ---- Source acquisition --------------------------------------------------

    /// <summary>Converts a local image without silently substituting a lettermark.</summary>
    public static void BuildFromFile(string sourcePath, string icoPath, string pngPath)
    {
        using var stream = File.OpenRead(sourcePath);
        using var image = Image.FromStream(stream);
        using var source = new Bitmap(image);
        using var trimmed = Trim(source);
        if (IsBlank(trimmed))
        {
            throw new InvalidDataException("The image is fully transparent.");
        }

        using var preview = Compose(trimmed, SourceSize, SourceSize, 1.0f);
        preview.Save(pngPath, ImageFormat.Png);
        WriteIco(trimmed, icoPath);
    }

    private static async Task<Bitmap> LoadSourceAsync(
        Uri? iconUrl,
        string appName,
        CancellationToken cancellationToken)
    {
        if (iconUrl is null)
        {
            return DrawLettermark(appName);
        }

        var isVector = iconUrl.AbsolutePath.EndsWith(".svg", StringComparison.OrdinalIgnoreCase);

        // A vector always renders better through a browser than through GDI+,
        // which cannot read SVG or WebP at all.
        if (!isVector)
        {
            var raster = await TryDownloadRasterAsync(iconUrl, cancellationToken);
            if (raster is not null && Math.Max(raster.Width, raster.Height) >= 96)
            {
                return raster;
            }

            var rendered = await TryRenderInBrowserAsync(iconUrl, cancellationToken);
            if (rendered is not null)
            {
                raster?.Dispose();
                return rendered;
            }

            if (raster is not null)
            {
                return raster;
            }
        }
        else
        {
            var rendered = await TryRenderInBrowserAsync(iconUrl, cancellationToken);
            if (rendered is not null)
            {
                return rendered;
            }
        }

        return DrawLettermark(appName);
    }

    private static async Task<Bitmap?> TryDownloadRasterAsync(Uri url, CancellationToken cancellationToken)
    {
        try
        {
            var bytes = await Http.GetByteArrayAsync(url, cancellationToken);
            using var stream = new MemoryStream(bytes);
            using var image = Image.FromStream(stream);
            return new Bitmap(image);
        }
        catch
        {
            // WebP, AVIF and malformed favicons all land here; the browser path handles them.
            return null;
        }
    }

    /// <summary>
    /// Renders an icon through headless Edge onto a transparent canvas. This is
    /// what makes SVG and WebP icons — which most large sites now publish — usable.
    /// </summary>
    private static async Task<Bitmap?> TryRenderInBrowserAsync(Uri url, CancellationToken cancellationToken)
    {
        var edge = FindEdge();
        if (edge is null)
        {
            return null;
        }

        var work = Path.Combine(Path.GetTempPath(), "mica-icon-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(work);
        var shot = Path.Combine(work, "icon.png");

        try
        {
            // The wrapper draws the icon at a size we choose: pointing the browser
            // straight at an SVG renders it at its intrinsic size, which is often tiny.
            //
            // The icon is drawn smaller than the window on purpose. Headless Edge takes
            // the window frame out of --window-size, and that frame is measured in
            // physical pixels: on a 200% display it costs 34 px of width and 98 px of
            // height. An icon sized to the window therefore overflows, gets clipped, and
            // has a scrollbar painted down its edge — which Trim() below then reads as
            // part of the artwork, leaving a grey bar baked into every icon and the glyph
            // pushed off centre. Leaving room means the icon lands whole on a transparent
            // canvas at any display scale, and Trim() crops back to the glyph itself.
            var page = Path.Combine(work, "wrap.html");
            await File.WriteAllTextAsync(
                page,
                $$"""
                  <!doctype html><meta charset="utf-8">
                  <style>html,body{margin:0;padding:0;overflow:hidden;background:transparent}</style>
                  <body>
                  <img src="{{url}}" style="width:{{SourceSize}}px;height:{{SourceSize}}px;display:block">
                  </body>
                  """,
                cancellationToken);

            var startInfo = new ProcessStartInfo(edge)
            {
                UseShellExecute = false,
                CreateNoWindow = true,
            };

            foreach (var argument in new[]
            {
                "--headless=new",
                "--disable-gpu",
                "--no-first-run",
                "--no-default-browser-check",
                "--hide-scrollbars",
                $"--user-data-dir={Path.Combine(work, "profile")}",
                "--default-background-color=00000000",
                $"--window-size={ViewportSize},{ViewportSize}",
                $"--screenshot={shot}",
                page,
            })
            {
                startInfo.ArgumentList.Add(argument);
            }

            using var process = Process.Start(startInfo);
            if (process is null)
            {
                return null;
            }

            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(30));
            await process.WaitForExitAsync(timeout.Token);

            if (!File.Exists(shot))
            {
                return null;
            }

            using var stream = new MemoryStream(await File.ReadAllBytesAsync(shot, cancellationToken));
            using var image = Image.FromStream(stream);
            var bitmap = new Bitmap(image);
            return IsBlank(bitmap) ? null : bitmap;
        }
        catch
        {
            return null;
        }
        finally
        {
            try
            {
                Directory.Delete(work, recursive: true);
            }
            catch
            {
                // Temp cleanup is best effort.
            }
        }
    }

    private static string? FindEdge()
    {
        foreach (var root in new[]
        {
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
        })
        {
            var candidate = Path.Combine(root, "Microsoft", "Edge", "Application", "msedge.exe");
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        return null;
    }

    /// <summary>True when every pixel is transparent — a render that produced nothing.</summary>
    private static bool IsBlank(Bitmap bitmap)
    {
        for (var y = 0; y < bitmap.Height; y += 8)
        {
            for (var x = 0; x < bitmap.Width; x += 8)
            {
                if (bitmap.GetPixel(x, y).A > 8)
                {
                    return false;
                }
            }
        }

        return true;
    }

    // ---- Composition ---------------------------------------------------------

    /// <summary>Crops fully transparent margins so the glyph fills its tile.</summary>
    private static Bitmap Trim(Bitmap source)
    {
        var minX = source.Width;
        var minY = source.Height;
        var maxX = -1;
        var maxY = -1;

        for (var y = 0; y < source.Height; y++)
        {
            for (var x = 0; x < source.Width; x++)
            {
                if (source.GetPixel(x, y).A <= 8)
                {
                    continue;
                }

                minX = Math.Min(minX, x);
                maxX = Math.Max(maxX, x);
                minY = Math.Min(minY, y);
                maxY = Math.Max(maxY, y);
            }
        }

        if (maxX < 0)
        {
            return (Bitmap)source.Clone();
        }

        var rect = Rectangle.FromLTRB(minX, minY, maxX + 1, maxY + 1);
        var trimmed = new Bitmap(rect.Width, rect.Height, PixelFormat.Format32bppArgb);
        using var graphics = Graphics.FromImage(trimmed);
        graphics.DrawImage(source, new Rectangle(0, 0, rect.Width, rect.Height), rect, GraphicsUnit.Pixel);
        return trimmed;
    }

    private static Bitmap Compose(Bitmap source, int width, int height, float fill)
    {
        var canvas = new Bitmap(width, height, PixelFormat.Format32bppArgb);
        canvas.SetResolution(96, 96);

        using var graphics = Graphics.FromImage(canvas);
        graphics.SmoothingMode = SmoothingMode.AntiAlias;
        graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
        graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;
        graphics.CompositingQuality = CompositingQuality.HighQuality;

        var target = Math.Min(width, height) * fill;
        var scale = target / Math.Max(source.Width, source.Height);
        var drawWidth = source.Width * scale;
        var drawHeight = source.Height * scale;

        graphics.DrawImage(source, (width - drawWidth) / 2f, (height - drawHeight) / 2f, drawWidth, drawHeight);
        return canvas;
    }

    private static Bitmap DrawLettermark(string appName)
    {
        var canvas = new Bitmap(SourceSize, SourceSize, PixelFormat.Format32bppArgb);
        using var graphics = Graphics.FromImage(canvas);
        graphics.SmoothingMode = SmoothingMode.AntiAlias;
        graphics.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;

        // A stable colour per name, so the same site always gets the same tile.
        var hash = appName.Aggregate(17, (current, c) => (current * 31) + c);
        var hue = Math.Abs(hash) % 360;
        var accent = FromHsl(hue, 0.55, 0.45);

        using (var brush = new SolidBrush(accent))
        using (var path = RoundedRectangle(new Rectangle(16, 16, SourceSize - 32, SourceSize - 32), 96))
        {
            graphics.FillPath(brush, path);
        }

        var letter = string.IsNullOrWhiteSpace(appName)
            ? "?"
            : char.ToUpperInvariant(appName.TrimStart()[0]).ToString();

        using var font = new Font("Segoe UI", 240, FontStyle.Regular, GraphicsUnit.Pixel);
        using var text = new SolidBrush(Color.White);
        using var format = new StringFormat
        {
            Alignment = StringAlignment.Center,
            LineAlignment = StringAlignment.Center,
        };

        graphics.DrawString(letter, font, text, new RectangleF(0, 0, SourceSize, SourceSize), format);
        return canvas;
    }

    private static GraphicsPath RoundedRectangle(Rectangle bounds, int radius)
    {
        var diameter = radius * 2;
        var path = new GraphicsPath();
        path.AddArc(bounds.X, bounds.Y, diameter, diameter, 180, 90);
        path.AddArc(bounds.Right - diameter, bounds.Y, diameter, diameter, 270, 90);
        path.AddArc(bounds.Right - diameter, bounds.Bottom - diameter, diameter, diameter, 0, 90);
        path.AddArc(bounds.X, bounds.Bottom - diameter, diameter, diameter, 90, 90);
        path.CloseFigure();
        return path;
    }

    private static Color FromHsl(double hue, double saturation, double lightness)
    {
        var c = (1 - Math.Abs((2 * lightness) - 1)) * saturation;
        var x = c * (1 - Math.Abs((hue / 60 % 2) - 1));
        var m = lightness - (c / 2);

        var (r, g, b) = hue switch
        {
            < 60 => (c, x, 0d),
            < 120 => (x, c, 0d),
            < 180 => (0d, c, x),
            < 240 => (0d, x, c),
            < 300 => (x, 0d, c),
            _ => (c, 0d, x),
        };

        return Color.FromArgb(
            (int)Math.Round((r + m) * 255),
            (int)Math.Round((g + m) * 255),
            (int)Math.Round((b + m) * 255));
    }

    // ---- ICO writing ---------------------------------------------------------

    /// <summary>
    /// Writes a multi-resolution icon. Windows picks a different entry for the
    /// window corner, the taskbar, Alt+Tab and the Start menu, so all of them
    /// have to be present or the shell upscales a small one and it looks muddy.
    /// </summary>
    private static void WriteIco(Bitmap source, string path)
    {
        var payloads = new List<byte[]>();

        foreach (var size in IcoSizes)
        {
            using var image = Compose(source, size, size, 1.0f);
            payloads.Add(size >= 256 ? EncodePng(image) : EncodeDib(image));
        }

        using var file = File.Create(path);
        using var writer = new BinaryWriter(file);

        writer.Write((ushort)0);
        writer.Write((ushort)1);
        writer.Write((ushort)IcoSizes.Length);

        var offset = 6 + (16 * IcoSizes.Length);
        for (var i = 0; i < IcoSizes.Length; i++)
        {
            var size = IcoSizes[i];
            var dimension = (byte)(size >= 256 ? 0 : size);

            writer.Write(dimension);
            writer.Write(dimension);
            writer.Write((byte)0);
            writer.Write((byte)0);
            writer.Write((ushort)1);
            writer.Write((ushort)32);
            writer.Write((uint)payloads[i].Length);
            writer.Write((uint)offset);

            offset += payloads[i].Length;
        }

        foreach (var payload in payloads)
        {
            writer.Write(payload);
        }
    }

    private static byte[] EncodePng(Bitmap image)
    {
        using var stream = new MemoryStream();
        image.Save(stream, ImageFormat.Png);
        return stream.ToArray();
    }

    /// <summary>Encodes one icon entry as a bottom-up 32-bit DIB plus an empty AND mask.</summary>
    private static byte[] EncodeDib(Bitmap image)
    {
        var width = image.Width;
        var height = image.Height;
        var rowBytes = width * 4;
        var xorBytes = rowBytes * height;
        var maskRowBytes = ((width + 31) / 32) * 4;
        var maskBytes = maskRowBytes * height;

        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream);

        writer.Write(40u);
        writer.Write(width);
        writer.Write(height * 2);
        writer.Write((ushort)1);
        writer.Write((ushort)32);
        writer.Write(0u);
        writer.Write((uint)(xorBytes + maskBytes));
        writer.Write(0);
        writer.Write(0);
        writer.Write(0u);
        writer.Write(0u);

        var data = image.LockBits(
            new Rectangle(0, 0, width, height),
            ImageLockMode.ReadOnly,
            PixelFormat.Format32bppArgb);

        try
        {
            var pixels = new byte[xorBytes];
            for (var y = 0; y < height; y++)
            {
                Marshal.Copy(data.Scan0 + (y * data.Stride), pixels, y * rowBytes, rowBytes);
            }

            for (var y = height - 1; y >= 0; y--)
            {
                writer.Write(pixels, y * rowBytes, rowBytes);
            }
        }
        finally
        {
            image.UnlockBits(data);
        }

        writer.Write(new byte[maskBytes]);
        writer.Flush();
        return stream.ToArray();
    }
}
