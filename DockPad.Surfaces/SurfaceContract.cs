namespace DockPad.Surfaces;

public readonly record struct SurfaceKey(int Row, int Column);

public enum KeyState { Pressed, Released }

public readonly record struct KeyEvent(SurfaceKey Key, KeyState State);

/// <summary>
/// Pixels BGRA 32 bits, image droite : le pilote tourne et encode.
/// </summary>
public sealed record SurfaceImage(int Width, int Height, byte[] Bgra32)
{
    public int Width { get; } = Width > 0 ? Width : throw new ArgumentOutOfRangeException(nameof(Width), Width, "Largeur nulle ou négative.");

    public int Height { get; } = Height > 0 ? Height : throw new ArgumentOutOfRangeException(nameof(Height), Height, "Hauteur nulle ou négative.");

    /// <summary>
    /// Taille calculée en <c>long</c> : une grande image ne doit pas passer la garde par débordement.
    /// </summary>
    public byte[] Bgra32 { get; } = (Bgra32 ?? throw new ArgumentNullException(nameof(Bgra32))).LongLength == (long)Width * Height * 4
        ? Bgra32
        : throw new ArgumentException($"{Bgra32.Length} octets pour une image {Width}x{Height} BGRA (attendu {(long)Width * Height * 4}).", nameof(Bgra32));
}

public sealed record SurfaceDescription(string Model, int KeyRows, int KeyColumns, int KeyImageSize,
    int DisplayCount, int DisplayImageSize);

public sealed record SurfaceInfo(string DriverName, string DevicePath, string Model, string? SerialNumber);

public interface IControlSurface : IAsyncDisposable
{
    SurfaceDescription Description { get; }

    /// <summary>
    /// Levé hors du thread UI.
    /// </summary>
    event Action<KeyEvent>? KeyChanged;

    /// <summary>
    /// Levé une seule fois, puis la surface est inutilisable.
    /// </summary>
    event Action<Exception>? Disconnected;

    Task SetKeyImageAsync(SurfaceKey key, SurfaceImage image, CancellationToken ct);

    Task SetDisplayImageAsync(int display, SurfaceImage image, CancellationToken ct);

    Task ClearAsync(CancellationToken ct);

    /// <summary>
    /// Rien n'est visible avant CommitAsync.
    /// </summary>
    Task CommitAsync(CancellationToken ct);

    Task SleepAsync(CancellationToken ct);
}

public interface IControlSurfaceDriver
{
    string Name { get; }

    /// <summary>
    /// Branchement ou débranchement, levé hors du thread UI.
    /// </summary>
    event Action? SurfacesChanged;

    IReadOnlyList<SurfaceInfo> FindSurfaces();

    /// <summary>
    /// Lève SurfaceBusyException si la surface est occupée.
    /// </summary>
    Task<IControlSurface> OpenAsync(SurfaceInfo surface, CancellationToken ct);
}

public sealed class SurfaceBusyException(string message, Exception? inner = null) : Exception(message, inner);
