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
    public static async Task<BitmapImage?> LoadAsync(string path)
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
            await bitmap.SetSourceAsync(stream);
            return bitmap;
        }
        catch
        {
            return null;
        }
    }
}
