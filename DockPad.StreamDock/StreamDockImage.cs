using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace DockPad.StreamDock;

internal static class StreamDockImage
{
    private const int JpegQuality = 95, MinimumJpegQuality = 35, BootImageJpegQuality = 85;
    private const double ScreenDpi = 96;

    /// <summary>
    /// Carré de côté size, tourné de 90° antihoraire ; renvoyé à qualité minimale même au-delà du plafond.
    /// </summary>
    public static byte[] ToJpeg(SurfaceImage image, int size)
        => ToJpeg(image, size, maxLength: StreamDockModel.MaxJpegLength);

    internal static byte[] ToJpeg(SurfaceImage image, int size, int maxLength)
        => EncodeJpegAtBestQuality(image, size, maxLength);

    /// <summary>
    /// Rotation antihoraire sans redimensionnement ; null si le JPEG dépasse la limite à la qualité minimale.
    /// </summary>
    public static byte[]? ToJpeg(SurfaceImage image)
        => EncodeJpeg(image, size: null, maxLength: StreamDockModel.MaxJpegLength);

    /// <summary>
    /// Image de démarrage tournée de 90° antihoraire, en qualité fixe ; le plafond est celui du tampon du dock.
    /// </summary>
    public static byte[] ToBootJpeg(SurfaceImage image)
    {
        var jpeg = Encode(Rotate(image, size: null), BootImageJpegQuality);
        if (jpeg.Length > StreamDockModel.MaxBootJpegLength)
            throw new ArgumentException($"Boot image JPEG is {jpeg.Length} bytes, over the {StreamDockModel.MaxBootJpegLength} byte limit.", nameof(image));
        return jpeg;
    }

    internal static byte[]? EncodeJpeg(SurfaceImage image, int? size, int maxLength)
    {
        var jpeg = EncodeJpegAtBestQuality(image, size, maxLength);
        return jpeg.Length <= maxLength ? jpeg : null;
    }

    private static byte[] EncodeJpegAtBestQuality(SurfaceImage image, int? size, int maxLength)
    {
        var frame = Rotate(image, size);
        for (var quality = JpegQuality; ; quality -= 10)
        {
            var jpeg = Encode(frame, quality);
            if (jpeg.Length <= maxLength || quality == MinimumJpegQuality)
                return jpeg;
        }
    }

    private static BitmapFrame Rotate(SurfaceImage image, int? size)
    {
        var source = BitmapSource.Create(image.Width, image.Height, ScreenDpi, ScreenDpi, PixelFormats.Bgra32,
            palette: null, image.Bgra32, stride: image.Width * 4);

        var transform = new TransformGroup();
        if (size != null && (image.Width != size.Value || image.Height != size.Value))
            transform.Children.Add(new ScaleTransform((double)size.Value / image.Width, (double)size.Value / image.Height));
        transform.Children.Add(new RotateTransform(-90));

        var rotated = new TransformedBitmap(source, transform);
        var opaque = new FormatConvertedBitmap(rotated, PixelFormats.Bgr24, destinationPalette: null, alphaThreshold: 0);

        return BitmapFrame.Create(opaque);
    }

    private static byte[] Encode(BitmapFrame frame, int quality)
    {
        var encoder = new JpegBitmapEncoder { QualityLevel = quality };
        encoder.Frames.Add(frame);
        using var jpeg = new MemoryStream();
        encoder.Save(jpeg);
        return jpeg.ToArray();
    }
}
