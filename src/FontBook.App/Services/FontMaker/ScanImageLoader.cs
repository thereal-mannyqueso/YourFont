using System.Windows.Media;
using System.Windows.Media.Imaging;
using FontBook.Core.FontMaker;

namespace FontBook.App.Services.FontMaker;

/// <summary>Loads a scanned or photographed template image for the Font Maker.</summary>
public interface IScanImageLoader
{
    /// <summary>Decodes the image (honoring phone-camera rotation) into grayscale pixels, plus a small preview.</summary>
    /// <exception cref="NotSupportedException">The file isn't an image WPF can read.</exception>
    (GrayImage Image, ImageSource Preview) Load(string path);
}

public sealed class ScanImageLoader : IScanImageLoader
{
    /// <summary>Very large photos are scaled down to this width; more detail doesn't help.</summary>
    private const int MaxWidth = 3600;

    public (GrayImage Image, ImageSource Preview) Load(string path)
    {
        BitmapSource source;
        using (var stream = File.OpenRead(path))
        {
            var decoder = BitmapDecoder.Create(stream, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
            source = decoder.Frames[0];
        }

        source = ApplyCameraRotation(source);
        if (source.PixelWidth > MaxWidth)
        {
            var factor = MaxWidth / (double)source.PixelWidth;
            source = new TransformedBitmap(source, new ScaleTransform(factor, factor));
        }

        var gray = new FormatConvertedBitmap(source, PixelFormats.Gray8, null, 0);
        int width = gray.PixelWidth, height = gray.PixelHeight;
        var pixels = new byte[width * height];
        gray.CopyPixels(pixels, width, 0);

        var previewScale = 360.0 / width;
        var preview = new TransformedBitmap(gray, new ScaleTransform(previewScale, previewScale));
        preview.Freeze();
        return (new GrayImage(width, height, pixels), preview);
    }

    /// <summary>Phone photos often store "rotate me" in EXIF instead of rotating the pixels.</summary>
    private static BitmapSource ApplyCameraRotation(BitmapSource source)
    {
        ushort orientation = 1;
        try
        {
            if (source.Metadata is BitmapMetadata metadata && metadata.GetQuery("System.Photo.Orientation") is ushort value)
            {
                orientation = value;
            }
        }
        catch (Exception ex) when (ex is NotSupportedException or InvalidOperationException or ArgumentException)
        {
            // No readable metadata (e.g. PNG scans) — use the pixels as they are.
        }

        var angle = orientation switch { 3 => 180, 6 => 90, 8 => 270, _ => 0 };
        return angle == 0 ? source : new TransformedBitmap(source, new RotateTransform(angle));
    }
}
