using DockPad.Surfaces;

namespace DockPad.Tests;

/// <summary>
/// Images cohérentes acceptées, dimensions et longueurs invalides refusées, y compris les débordements.
/// Description avec ou sans colonne, dimensions de la colonne et anciens écrans conservés.
/// </summary>
public class SurfaceContractTests
{
    [Fact]
    public void Description_ColonneOptionnelle_ConserveLesDimensions()
    {
        var withStrip = new SurfaceDescription("Strip", 3, 5, 96, 3, 80, new SurfaceSize(75, 380));
        var withoutStrip = new SurfaceDescription("Keys", 3, 5, 96, 0, 0, SideStrip: null);

        Assert.Equal(new SurfaceSize(75, 380), withStrip.SideStrip);
        Assert.Null(withoutStrip.SideStrip);
        Assert.Equal(3, withStrip.DisplayCount);
        Assert.Equal(80, withStrip.DisplayImageSize);
    }

    [Fact]
    public void SurfaceImage_TailleIncoherente_Refusee()
        => Assert.Throws<ArgumentException>(() => new SurfaceImage(96, 96, new byte[10]));

    [Fact]
    public void SurfaceImage_TailleQuiDeborde_Refusee()
        => Assert.Throws<ArgumentException>(() => new SurfaceImage(65536, 65536, []));

    [Theory]
    [InlineData(0, 96)]
    [InlineData(96, 0)]
    [InlineData(-1, -1)]
    public void SurfaceImage_TailleNulleOuNegative_Refusee(int width, int height)
        => Assert.ThrowsAny<ArgumentException>(() => new SurfaceImage(width, height, []));

    [Fact]
    public void SurfaceImage_TailleCoherente_Acceptee()
    {
        var image = new SurfaceImage(Width: 2, Height: 3, Bgra32: new byte[2 * 3 * 4]);

        Assert.Equal(2, image.Width);
        Assert.Equal(3, image.Height);
        Assert.Equal(24, image.Bgra32.Length);
    }
}
