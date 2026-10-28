using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using DockPad.Services.StreamDock;

namespace DockPad.Tests.StreamDock;

/// <summary>
/// Image de démarrage : celle de DockPad à la taille du dock, avec le logo sur la touche du centre ; un fichier d'une
/// autre taille, absent ou illisible n'est pas retenu et laisse l'image de DockPad ; un fichier à la bonne taille l'est.
/// </summary>
public sealed class DockBootImageTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "dockpad-boot-image-" + Guid.NewGuid().ToString("N"));

    public DockBootImageTests() => Directory.CreateDirectory(_directory);

    public void Dispose()
    {
        try { Directory.Delete(_directory, recursive: true); }
        catch (IOException) { }
    }

    [Fact]
    public async Task ParDefaut_ImageDockPadALaTailleDuDock()
    {
        var image = await OnStaThread(() => DockBootImage.ToSurfaceImage(DockBootImage.Default()));

        Assert.Equal(854, image.Width);
        Assert.Equal(480, image.Height);
        var (blue, green, red) = PixelAt(image, x: 340, y: 210);
        Assert.True(red + green + blue > 250, $"Logo attendu sur la touche du centre, pixel {red},{green},{blue}.");
        Assert.Equal((0x1A, 0x17, 0x16), PixelAt(image, x: 2, y: 2));
    }

    [Fact]
    public async Task Fichier_BonneTaille_Retenu()
    {
        string path = WritePng(854, 480, Colors.Red);

        var image = await OnStaThread(() => DockBootImage.ToSurfaceImage(DockBootImage.Load(path)));

        Assert.Equal((0, 0, 255), PixelAt(image, x: 340, y: 210));
    }

    [Fact]
    public async Task Fichier_AutreTaille_RefuseEtImageDockPad()
    {
        string path = WritePng(800, 600, Colors.Red);

        var (read, loaded) = await OnStaThread(() => (DockBootImage.Read(path), DockBootImage.ToSurfaceImage(DockBootImage.Load(path))));

        Assert.NotNull(read);
        Assert.False(DockBootImage.HasBootSize(read));
        Assert.Equal(854, loaded.Width);
        Assert.NotEqual((0, 0, 255), PixelAt(loaded, x: 340, y: 210));
    }

    [Fact]
    public async Task FichierAbsentOuIllisible_ImageDockPad()
    {
        string missing = Path.Combine(_directory, "absent.png");
        string unreadable = Path.Combine(_directory, "texte.png");
        File.WriteAllText(unreadable, "pas une image");

        var (missingRead, unreadableRead, loaded) = await OnStaThread(() =>
            (DockBootImage.Read(missing), DockBootImage.Read(unreadable), DockBootImage.Load(unreadable)));

        Assert.Null(missingRead);
        Assert.Null(unreadableRead);
        Assert.True(DockBootImage.HasBootSize(loaded));
    }

    private string WritePng(int width, int height, Color color)
    {
        string path = Path.Combine(_directory, $"{width}x{height}.png");
        var pixels = new byte[width * height * 4];
        for (int offset = 0; offset < pixels.Length; offset += 4)
        {
            pixels[offset] = color.B;
            pixels[offset + 1] = color.G;
            pixels[offset + 2] = color.R;
            pixels[offset + 3] = 255;
        }
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(BitmapSource.Create(width, height, 96, 96, PixelFormats.Bgra32, null, pixels, width * 4)));
        using var stream = File.Create(path);
        encoder.Save(stream);
        return path;
    }

    private static (int Blue, int Green, int Red) PixelAt(DockPad.Surfaces.SurfaceImage image, int x, int y)
    {
        int offset = (y * image.Width + x) * 4;
        return (image.Bgra32[offset], image.Bgra32[offset + 1], image.Bgra32[offset + 2]);
    }

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
