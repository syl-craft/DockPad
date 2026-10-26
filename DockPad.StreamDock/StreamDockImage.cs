using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace DockPad.StreamDock;

internal static class StreamDockImage
{
    private const int JpegQuality = 95;
    private const double ScreenDpi = 96;

    /// <summary>
    /// Image droite vers le JPEG attendu par le dock : carré de côté size, tourné de 90° antihoraire.
    /// </summary>
    public static byte[] ToJpeg(SurfaceImage image, int size)
    {
        var source = BitmapSource.Create(image.Width, image.Height, ScreenDpi, ScreenDpi, PixelFormats.Bgra32,
            palette: null, image.Bgra32, stride: image.Width * 4);

        var transform = new TransformGroup();
        if (image.Width != size || image.Height != size)
            transform.Children.Add(new ScaleTransform((double)size / image.Width, (double)size / image.Height));
        transform.Children.Add(new RotateTransform(-90));

        var rotated = new TransformedBitmap(source, transform);
        var opaque = new FormatConvertedBitmap(rotated, PixelFormats.Bgr24, destinationPalette: null, alphaThreshold: 0);

        var encoder = new JpegBitmapEncoder { QualityLevel = JpegQuality };
        encoder.Frames.Add(BitmapFrame.Create(opaque));
        using var jpeg = new MemoryStream();
        encoder.Save(jpeg);
        return jpeg.ToArray();
    }
}
