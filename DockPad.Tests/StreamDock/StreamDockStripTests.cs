using DockPad.StreamDock;
using DockPad.Surfaces;

namespace DockPad.Tests;

/// <summary>
/// Découpe de la colonne : tailles et zones, lignes communes, pixels conservés et marges noires opaques.
/// </summary>
public class StreamDockStripTests
{
    [Fact]
    public void Decoupe_TroisTranchesAuxBonnesTailles()
    {
        var slices = StreamDockStrip.Slice(Strip());

        Assert.Equal(new byte[] { 16, 17, 18 }, slices.Select(slice => slice.ZoneId));
        Assert.Equal(new[] { (80, 152), (80, 152), (80, 120) },
            slices.Select(slice => (slice.Image.Width, slice.Image.Height)));
    }

    [Fact]
    public void Decoupe_LignesCommunesIdentiques()
    {
        var slices = StreamDockStrip.Slice(Strip());

        Assert.Equal(slices[0].Image.Bgra32.Skip(150 * 80 * 4).ToArray(),
            slices[1].Image.Bgra32.Take(2 * 80 * 4).ToArray());
        Assert.Equal(slices[1].Image.Bgra32.Skip(150 * 80 * 4).ToArray(),
            slices[2].Image.Bgra32.Take(2 * 80 * 4).ToArray());
    }

    [Fact]
    public void Decoupe_NoirHorsZoneUtile()
    {
        var strip = Strip();
        var slices = StreamDockStrip.Slice(strip);
        for (var index = 0; index < slices.Count; index++)
        {
            var image = slices[index].Image;
            for (var y = 0; y < image.Height; y++)
            {
                for (var x = 0; x < image.Width; x++)
                {
                    var originalY = index * 150 + y;
                    var pixel = image.Bgra32.AsSpan((y * image.Width + x) * 4, 4).ToArray();
                    var expected = x >= 75 || originalY >= 380
                        ? new byte[] { 0, 0, 0, 255 }
                        : strip.Bgra32.AsSpan((originalY * 75 + x) * 4, 4).ToArray();
                    Assert.Equal(expected, pixel);
                }
            }
        }
    }

    private static SurfaceImage Strip()
    {
        var pixels = new byte[75 * 380 * 4];
        new Random(293).NextBytes(pixels);
        for (var offset = 3; offset < pixels.Length; offset += 4)
            pixels[offset] = 255;
        return new SurfaceImage(75, 380, pixels);
    }
}
