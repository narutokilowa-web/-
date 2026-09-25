using SkiaSharp;

namespace Mfop.Evidence.Application;

/// <summary>
/// Derived images for viewing: decoded and re-encoded as new JPEGs, so no EXIF (and no GPS) from the original is copied.
/// Originals are kept byte-for-byte; only the auditor may download them.
/// </summary>
public static class PreviewRenderer
{
    /// <returns>JPEG bytes, or null if the original cannot be decoded as an image.</returns>
    public static byte[]? Render(byte[] original, int maxPixels, int quality = 80)
    {
        using var bitmap = SKBitmap.Decode(original);
        if (bitmap is null || bitmap.Width <= 0 || bitmap.Height <= 0)
        {
            return null;
        }

        var scale = Math.Min(1.0, (double)maxPixels / Math.Max(bitmap.Width, bitmap.Height));
        var width = Math.Max(1, (int)Math.Round(bitmap.Width * scale));
        var height = Math.Max(1, (int)Math.Round(bitmap.Height * scale));
        using var resized = bitmap.Resize(new SKImageInfo(width, height), new SKSamplingOptions(SKFilterMode.Linear, SKMipmapMode.Linear));
        using var image = SKImage.FromBitmap(resized ?? bitmap);
        using var data = image.Encode(SKEncodedImageFormat.Jpeg, quality);
        return data?.ToArray();
    }
}
