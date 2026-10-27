using System.Globalization;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using DockPad.StreamDock;
using DockPad.Surfaces;

namespace StreamDockProbe;

/// <summary>
/// Images préparées sur le thread STA principal, avant toute attente asynchrone.
/// </summary>
internal sealed class ProbeImages
{
    private const int KeySize = 96;
    private static readonly Color[] RowColors = [Color.FromRgb(32, 96, 160), Color.FromRgb(34, 125, 72), Color.FromRgb(151, 66, 111)];

    public Dictionary<SurfaceKey, SurfaceImage> Normal { get; } = [];
    public Dictionary<SurfaceKey, SurfaceImage> Pressed { get; } = [];
    public SurfaceImage SideStrip { get; } = DrawSideStrip();
    public SurfaceImage Noise { get; private set; } = new(KeySize, KeySize, new byte[KeySize * KeySize * 4]);
    public SurfaceImage Red { get; private set; } = new(KeySize, KeySize, new byte[KeySize * KeySize * 4]);
    public int NoiseJpegBytes { get; private set; }
    public int NoiseChunks { get; private set; }

    public static ProbeImages Create(CancellationToken ct)
    {
        if (Thread.CurrentThread.GetApartmentState() != ApartmentState.STA)
            throw new InvalidOperationException("Probe images must be rendered on the main STA thread.");

        var images = new ProbeImages();
        for (var row = 0; row < StreamDockModel.KeyRows; row++)
        {
            for (var column = 0; column < StreamDockModel.KeyColumns; column++)
            {
                ct.ThrowIfCancellationRequested();
                var key = new SurfaceKey(row, column);
                var normal = DrawLabel($"L{row + 1} C{column + 1}", KeySize, RowColors[row], fontSize: 25);
                images.Normal.Add(key, normal);
                images.Pressed.Add(key, DrawPressed(normal, RowColors[row]));
            }
        }
        images.Red = DrawLabel("", KeySize, Colors.Red, fontSize: 25);
        var pixels = new byte[KeySize * KeySize * 4];
        Random.Shared.NextBytes(pixels);
        for (var offset = 3; offset < pixels.Length; offset += 4)
        {
            pixels[offset] = 255;
        }
        images.Noise = new SurfaceImage(KeySize, KeySize, pixels);
        var jpeg = StreamDockImage.ToJpeg(images.Noise, KeySize);
        if (jpeg.Length <= StreamDockModel.ChunkLength)
            throw new InvalidOperationException("Noise JPEG must exceed one HID chunk.");
        images.NoiseJpegBytes = jpeg.Length;
        images.NoiseChunks = StreamDockProtocol.Image(StreamDockModel.KeyId(new SurfaceKey(0, 0)), jpeg).Count - 1;
        return images;
    }

    private static SurfaceImage DrawSideStrip()
    {
        const int border = 2;
        var width = StreamDockModel.SideStrip.Width;
        var height = StreamDockModel.SideStrip.Height;
        var pixels = new byte[width * height * 4];
        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                var offset = (y * width + x) * 4;
                var frame = x < border || x >= width - border || y < border || y >= height - border;
                var graduation = y % 50 == 0 && x < 20;
                pixels[offset] = frame || graduation ? (byte)0 : (byte)20;
                pixels[offset + 1] = frame || graduation ? (byte)255 : (byte)20;
                pixels[offset + 2] = frame || graduation ? (byte)0 : (byte)20;
                pixels[offset + 3] = 255;
            }
        }
        return new SurfaceImage(width, height, pixels);
    }

    private static SurfaceImage DrawLabel(string label, int size, Color background, double fontSize)
    {
        var visual = new DrawingVisual();
        using (var drawing = visual.RenderOpen())
        {
            drawing.DrawRectangle(new SolidColorBrush(background), pen: null, new Rect(0, 0, size, size));
            var text = new FormattedText(label, CultureInfo.GetCultureInfo("fr-FR"), FlowDirection.LeftToRight,
                new Typeface(new FontFamily("Segoe UI"), FontStyles.Normal, FontWeights.Bold, FontStretches.Normal),
                fontSize, Brushes.White, pixelsPerDip: 1);
            drawing.DrawText(text, new Point((size - text.Width) / 2, (size - text.Height) / 2));
        }
        return Render(visual, size);
    }

    private static SurfaceImage DrawPressed(SurfaceImage normal, Color background)
    {
        var lighter = Color.FromRgb(Lighten(background.R), Lighten(background.G), Lighten(background.B));
        var source = BitmapSource.Create(normal.Width, normal.Height, 96, 96, PixelFormats.Bgra32,
            palette: null, normal.Bgra32, stride: normal.Width * 4);
        var visual = new DrawingVisual();
        using (var drawing = visual.RenderOpen())
        {
            drawing.DrawRectangle(new SolidColorBrush(lighter), pen: null, new Rect(0, 0, KeySize, KeySize));
            const double pressedSize = KeySize * 0.85;
            const double inset = (KeySize - pressedSize) / 2;
            drawing.DrawImage(source, new Rect(inset, inset, pressedSize, pressedSize));
        }
        return Render(visual, KeySize);
    }

    private static byte Lighten(byte component) => (byte)(component + (255 - component) * 0.25);

    private static SurfaceImage Render(DrawingVisual visual, int size)
    {
        var bitmap = new RenderTargetBitmap(size, size, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(visual);
        var opaque = new FormatConvertedBitmap(bitmap, PixelFormats.Bgra32, destinationPalette: null, alphaThreshold: 0);
        var pixels = new byte[size * size * 4];
        opaque.CopyPixels(pixels, stride: size * 4, offset: 0);
        for (var offset = 3; offset < pixels.Length; offset += 4)
        {
            pixels[offset] = 255;
        }
        return new SurfaceImage(size, size, pixels);
    }
}
