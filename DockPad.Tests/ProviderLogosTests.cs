using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using DockPad.Services;

namespace DockPad.Tests;

/// <summary>
/// Le logo d'Antigravity rendu pour l'icône du prédéfini : c'est le seul chemin où un tracé du
/// bandeau devient un fichier, et un rendu vide ou décentré ne se verrait que dans le menu de
/// Windows.
/// </summary>
public class ProviderLogosTests
{
    private static BitmapSource Decode(byte[] png) =>
        BitmapDecoder.Create(new MemoryStream(png), BitmapCreateOptions.None, BitmapCacheOption.OnLoad).Frames[0];

    private static (byte B, byte G, byte R, byte A) PixelAt(BitmapSource image, int x, int y)
    {
        var pixel = new byte[4];
        new FormatConvertedBitmap(image, PixelFormats.Bgra32, null, 0)
            .CopyPixels(new System.Windows.Int32Rect(x, y, 1, 1), pixel, 4, 0);
        return (pixel[0], pixel[1], pixel[2], pixel[3]);
    }

    [Fact]
    public void RenderPng_RendUnCarreDeLaTailleDemandee()
    {
        var image = Decode(ProviderLogos.RenderPng(ProviderLogos.Antigravity, ProviderLogos.AntigravityBrush, 64));

        Assert.Equal(64, image.PixelWidth);
        Assert.Equal(64, image.PixelHeight);
    }

    [Fact]
    public void RenderPng_PeintLeTraceDansSaCouleurSurFondTransparent()
    {
        var image = Decode(ProviderLogos.RenderPng(ProviderLogos.Antigravity, ProviderLogos.AntigravityBrush, 64));

        // Le coin haut gauche est hors de l'arche, son sommet est plein.
        Assert.Equal(0, PixelAt(image, 0, 0).A);

        var top = PixelAt(image, 32, 8);
        Assert.Equal(255, top.A);
        Assert.Equal((0x1A, 0x73, 0xE8), (top.R, top.G, top.B));
    }
}
