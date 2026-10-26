using DockPad.Surfaces;

namespace DockPad.Tests;

/// <summary>
/// Contrat de surface de contrôle : une image dont la taille ne colle pas à ses pixels est
/// refusée dès la construction, y compris quand largeur × hauteur × 4 déborde un entier, une
/// dimension nulle ou négative aussi, et une image cohérente est acceptée telle quelle.
/// </summary>
public class SurfaceContractTests
{
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
