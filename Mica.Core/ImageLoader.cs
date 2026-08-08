using Microsoft.UI.Xaml.Media.Imaging;
using Windows.Storage;

namespace Mica.Core;

/// <summary>
/// Loads images from ordinary file paths. WinUI's <see cref="BitmapImage"/> only
/// understands the ms-appx, ms-appdata and http schemes, and an unpackaged app has
/// none of those for a file it wrote itself — so the bytes go in through a stream.
/// </summary>
public static class ImageLoader
{
    /// <param name="decodePixelWidth">
    /// Decodes the image at this width rather than at its natural size. An icon
    /// drawn at 16 px otherwise keeps a 512 px surface in memory for nothing;
    /// the size it is shown at is enough. Zero keeps the natural size.
    /// <para>
    /// This is a count of physical pixels, not of the DIPs a control is sized in:
    /// a 36 px image needs 72 here on a 200% display, and asking for less leaves
    /// XAML enlarging the bitmap to fit. Decode for the largest scale worth
    /// supporting — shrinking a bitmap stays sharp, stretching one does not.
    /// </para>
    /// </param>
    public static async Task<BitmapImage?> LoadAsync(string path, int decodePixelWidth = 0)
    {
        try
        {
            if (!File.Exists(path))
            {
                return null;
            }

            var file = await StorageFile.GetFileFromPathAsync(path);
            using var stream = await file.OpenReadAsync();

            var bitmap = new BitmapImage();
            if (decodePixelWidth > 0)
            {
                bitmap.DecodePixelWidth = decodePixelWidth;
            }

            await bitmap.SetSourceAsync(stream);
            return bitmap;
        }
        catch
        {
            return null;
        }
    }
}
