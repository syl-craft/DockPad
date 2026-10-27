using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using DockPad.StreamDock;
using DockPad.Surfaces;

namespace DockPad.Tests;

/// <summary>
/// Encodage du 293S V3 sur un thread STA : rotation antihoraire sans miroir et réduction à la taille demandée.
/// Touches au-delà du plafond à qualité minimale, tranches rectangulaires refusées et images simples à qualité maximale.
/// </summary>
public class StreamDockImageTests
{
    [Theory]
    [InlineData(152)]
    [InlineData(120)]
    public async Task Jpeg_Rectangulaire_TourneSansRedimensionner(int height)
    {
        var pixels = new byte[80 * height * 4];
        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < 80; x++)
            {
                var offset = (y * 80 + x) * 4;
                pixels[offset + 2] = x < 20 && y < 20 ? (byte)255 : (byte)0;
                pixels[offset + 1] = x >= 60 && y < 20 ? (byte)255 : (byte)0;
                pixels[offset + 3] = 255;
            }
        }
        var image = new SurfaceImage(80, height, pixels);
        var decoded = await OnStaThread(() => Decode(StreamDockImage.ToJpeg(image)
            ?? throw new InvalidOperationException("Test image exceeds JPEG limit.")));

        Assert.Equal(height, decoded.PixelWidth);
        Assert.Equal(80, decoded.PixelHeight);
        Assert.True(IsRed(PixelAt(decoded, x: 5, y: 75)));
        Assert.True(IsGreen(PixelAt(decoded, x: 5, y: 5)));
        Assert.False(IsRed(PixelAt(decoded, x: height - 5, y: 75)));
    }

    [Fact]
    public async Task Jpeg_Rectangulaire_TropLourd_RenvoieNull()
    {
        var image = new SurfaceImage(80, 152, new byte[80 * 152 * 4]);

        Assert.Null(await OnStaThread(() => StreamDockImage.EncodeJpeg(image, size: null, maxLength: 1)));
    }

    [Fact]
    public async Task Jpeg_ToucheTropLourde_RenvoieLaQualiteMinimale()
    {
        const int size = 96;
        const int maxLength = 1;
        var image = RedTopLeftGreenTopRight(size);
        var minimum = await OnStaThread(() =>
        {
            var source = BitmapSource.Create(size, size, 96, 96, PixelFormats.Bgra32,
                palette: null, image.Bgra32, stride: size * 4);
            var rotated = new TransformedBitmap(source, new RotateTransform(-90));
            var opaque = new FormatConvertedBitmap(rotated, PixelFormats.Bgr24, destinationPalette: null, alphaThreshold: 0);
            var encoder = new JpegBitmapEncoder { QualityLevel = 35 };
            encoder.Frames.Add(BitmapFrame.Create(opaque));
            using var jpeg = new MemoryStream();
            encoder.Save(jpeg);
            return jpeg.ToArray();
        });

        var actual = await OnStaThread(() => StreamDockImage.ToJpeg(image, size, maxLength));

        Assert.True(actual.Length > maxLength);
        Assert.Equal(minimum, actual);
    }

    [Fact]
    public async Task Jpeg_TourneDUnQuartAntihoraire()
    {
        var decoded = await OnStaThread(() => Decode(StreamDockImage.ToJpeg(RedTopLeftCorner(96), size: 96)));

        Assert.Equal(96, decoded.PixelWidth);
        Assert.Equal(96, decoded.PixelHeight);
        Assert.True(IsRed(PixelAt(decoded, x: 5, y: 90)), "Le coin rouge doit finir en bas à gauche.");
        Assert.False(IsRed(PixelAt(decoded, x: 5, y: 5)), "Le haut gauche doit être noir.");
        Assert.False(IsRed(PixelAt(decoded, x: 90, y: 5)), "Le haut droit doit être noir.");
        Assert.False(IsRed(PixelAt(decoded, x: 90, y: 90)), "Le bas droit doit être noir.");
    }

    [Fact]
    public async Task Jpeg_RotationEtNonMiroir()
    {
        var decoded = await OnStaThread(() => Decode(StreamDockImage.ToJpeg(RedTopLeftGreenTopRight(96), size: 96)));

        Assert.True(IsRed(PixelAt(decoded, x: 5, y: 90)), "Le rouge (haut gauche) doit finir en bas à gauche.");
        Assert.True(IsGreen(PixelAt(decoded, x: 5, y: 5)), "Le vert (haut droit) doit finir en haut à gauche.");
        Assert.False(IsGreen(PixelAt(decoded, x: 90, y: 90)), "Un miroir mettrait le vert en bas à droite.");
    }

    [Fact]
    public async Task Jpeg_RameneALaTailleDemandee()
    {
        var decoded = await OnStaThread(() => Decode(StreamDockImage.ToJpeg(RedTopLeftCorner(160), size: 80)));

        Assert.Equal(80, decoded.PixelWidth);
        Assert.Equal(80, decoded.PixelHeight);
        Assert.True(IsRed(PixelAt(decoded, x: 5, y: 75)), "Le coin rouge doit finir en bas à gauche.");
    }

    [Fact]
    public async Task Jpeg_BruitTropLourd_ReduitSousLaLimite()
    {
        const int size = 96;
        var pixels = new byte[size * size * 4];
        new Random(293).NextBytes(pixels);
        for (var offset = 3; offset < pixels.Length; offset += 4)
        {
            pixels[offset] = 255;
        }
        var image = new SurfaceImage(size, size, pixels);
        var maximal = await OnStaThread(() => StreamDockImage.ToJpeg(image, size, maxLength: int.MaxValue));

        var jpeg = await OnStaThread(() => StreamDockImage.ToJpeg(image, size));

        Assert.True(maximal.Length > StreamDockModel.MaxJpegLength);
        Assert.InRange(jpeg.Length, 1, StreamDockModel.MaxJpegLength);
    }

    [Fact]
    public async Task Jpeg_ImageSimple_GardeLaQualiteMaximale()
    {
        const int size = 96;
        var image = new SurfaceImage(size, size, Enumerable.Repeat((byte)255, size * size * 4).ToArray());
        var maximal = await OnStaThread(() => StreamDockImage.ToJpeg(image, size, maxLength: int.MaxValue));

        var jpeg = await OnStaThread(() => StreamDockImage.ToJpeg(image, size));

        Assert.InRange(maximal.Length, 1, StreamDockModel.MaxJpegLength);
        Assert.Equal(maximal, jpeg);
    }

    /// <summary>
    /// Image noire dont le carré haut gauche, d'un quart de côté, est rouge.
    /// </summary>
    private static SurfaceImage RedTopLeftCorner(int size)
    {
        var pixels = new byte[size * size * 4];
        for (var y = 0; y < size; y++)
        {
            for (var x = 0; x < size; x++)
            {
                var offset = (y * size + x) * 4;
                pixels[offset + 2] = x < size / 4 && y < size / 4 ? (byte)255 : (byte)0;
                pixels[offset + 3] = 255;
            }
        }
        return new SurfaceImage(size, size, pixels);
    }

    /// <summary>
    /// Deux repères asymétriques : une rotation et un miroir ne les déplacent pas de la même façon.
    /// </summary>
    private static SurfaceImage RedTopLeftGreenTopRight(int size)
    {
        var image = RedTopLeftCorner(size);
        for (var y = 0; y < size / 4; y++)
        {
            for (var x = size - size / 4; x < size; x++)
            {
                image.Bgra32[(y * size + x) * 4 + 1] = 255;
            }
        }
        return image;
    }

    private static bool IsGreen((byte Blue, byte Green, byte Red) pixel)
        => pixel.Green > 180 && pixel.Red < 80 && pixel.Blue < 80;

    private static BitmapSource Decode(byte[] jpeg)
    {
        var decoder = new JpegBitmapDecoder(new MemoryStream(jpeg), BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
        var converted = new FormatConvertedBitmap(decoder.Frames[0], PixelFormats.Bgra32, null, 0);
        converted.Freeze();
        return converted;
    }

    private static (byte Blue, byte Green, byte Red) PixelAt(BitmapSource bitmap, int x, int y)
    {
        var pixel = new byte[4];
        bitmap.CopyPixels(new System.Windows.Int32Rect(x, y, 1, 1), pixel, 4, 0);
        return (pixel[0], pixel[1], pixel[2]);
    }

    /// <summary>
    /// Tolérance large : le JPEG altère les couleurs, surtout près des bords du carré.
    /// </summary>
    private static bool IsRed((byte Blue, byte Green, byte Red) pixel)
        => pixel.Red > 180 && pixel.Green < 80 && pixel.Blue < 80;

    private static async Task<T> OnStaThread<T>(Func<T> action)
    {
        var done = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            try { done.SetResult(action()); }
            catch (Exception ex) { done.SetException(ex); }
        }) { IsBackground = true };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        return await done.Task.WaitAsync(TimeSpan.FromSeconds(10));
    }
}
