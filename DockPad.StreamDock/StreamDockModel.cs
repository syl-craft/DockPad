namespace DockPad.StreamDock;

/// <summary>
/// Constantes et numérotation du MiraBox Stream Dock 293S V3.
/// </summary>
internal static class StreamDockModel
{
    public const ushort VendorId = 0x6603;
    public static readonly ushort[] ProductIds = [0x1014, 0x1005];
    public const ushort UsagePage = 0xFFA0, Usage = 1;
    public const int OutputReportLength = 1025, InputReportLength = 513, ChunkLength = 1024;

    /// <summary>
    /// Limite constatée sur le dock vers 10 Ko ; marge conservée pour l'envoi des JPEG.
    /// </summary>
    public const int MaxJpegLength = 8 * 1024;

    public const int KeyRows = 3, KeyColumns = 5;

    /// <summary>
    /// Numéro matériel de chaque touche, lignes de haut en bas, colonnes de gauche à droite.
    /// </summary>
    private static readonly byte[,] KeyIds =
    {
        { 13, 10, 7, 4, 1 },
        { 14, 11, 8, 5, 2 },
        { 15, 12, 9, 6, 3 },
    };

    internal const byte FirstDisplayId = 16;
    public static readonly SurfaceSize SideStrip = new(75, 380);
    public const int StripWidth = 80, StripHeight = 420, StripStep = 150;
    public static IReadOnlyList<int> StripSliceHeights { get; } = Array.AsReadOnly(new[] { 152, 152, 120 });

    public static byte KeyId(SurfaceKey key)
    {
        if (key.Row < 0 || key.Row >= KeyRows)
            throw new ArgumentOutOfRangeException(nameof(key), key, "Row outside the 3 x 5 grid.");
        if (key.Column < 0 || key.Column >= KeyColumns)
            throw new ArgumentOutOfRangeException(nameof(key), key, "Column outside the 3 x 5 grid.");

        return KeyIds[key.Row, key.Column];
    }

    public static SurfaceKey? KeyOf(byte keyId)
    {
        for (var row = 0; row < KeyRows; row++)
        {
            for (var column = 0; column < KeyColumns; column++)
            {
                if (KeyIds[row, column] == keyId)
                    return new SurfaceKey(row, column);
            }
        }
        return null;
    }

}
