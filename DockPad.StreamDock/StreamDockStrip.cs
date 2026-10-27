namespace DockPad.StreamDock;

internal sealed record StripSlice(byte ZoneId, SurfaceImage Image);

/// <summary>
/// Colonne utile complétée de noir opaque ; les tranches voisines partagent deux lignes.
/// </summary>
internal static class StreamDockStrip
{
    internal static IReadOnlyList<StripSlice> Slice(SurfaceImage strip)
    {
        if (strip.Width != StreamDockModel.SideStrip.Width || strip.Height != StreamDockModel.SideStrip.Height)
            throw new ArgumentException("Side strip image must be 75 x 380 pixels.", nameof(strip));

        var padded = new byte[StreamDockModel.StripWidth * StreamDockModel.StripHeight * 4];
        for (var offset = 3; offset < padded.Length; offset += 4)
            padded[offset] = 255;
        for (var y = 0; y < strip.Height; y++)
            strip.Bgra32.AsSpan(y * strip.Width * 4, strip.Width * 4)
                .CopyTo(padded.AsSpan(y * StreamDockModel.StripWidth * 4));

        var slices = new List<StripSlice>();
        var heights = StreamDockModel.StripSliceHeights;
        for (var index = 0; index < heights.Count; index++)
        {
            var pixels = padded.AsSpan(index * StreamDockModel.StripStep * StreamDockModel.StripWidth * 4,
                StreamDockModel.StripWidth * heights[index] * 4).ToArray();
            slices.Add(new StripSlice((byte)(StreamDockModel.FirstDisplayId + index),
                new SurfaceImage(StreamDockModel.StripWidth, heights[index], pixels)));
        }
        return slices;
    }
}
