using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using DockPad.Surfaces;

namespace DockPad.Services.StreamDock;

/// <summary>
/// Image de démarrage du Stream Dock : celle de DockPad, ou un fichier de l'utilisateur à la taille exacte du dock.
/// </summary>
public static class DockBootImage
{
    /// <summary>
    /// Écran entier du 293S V3, touches et colonne comprises ; aucune autre taille n'est envoyée.
    /// </summary>
    public static readonly SurfaceSize Size = new(854, 480);

    /// <summary>
    /// Lue dans les ressources de l'assembly : un pack URI se résout dans l'assembly hôte, qui n'est pas DockPad sous un test.
    /// </summary>
    public static BitmapSource Default()
    {
        var resources = new System.Resources.ResourceManager("DockPad.g", typeof(DockBootImage).Assembly);
        using var stream = resources.GetStream("assets/streamdockboot.png")
            ?? throw new InvalidOperationException("Default Stream Dock boot image is missing from the resources.");
        return Decode(stream);
    }

    /// <summary>
    /// Image du fichier, ou null s'il est absent ou illisible.
    /// </summary>
    public static BitmapSource? Read(string path)
    {
        try
        {
            using var stream = File.OpenRead(path);
            return Decode(stream);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException
            or FileFormatException or ArgumentException)
        {
            LogService.Warn(ex, "Stream Dock : image de démarrage illisible.");
            return null;
        }
    }

    /// <summary>
    /// Fichier choisi s'il est lisible et à la bonne taille, sinon l'image de DockPad.
    /// </summary>
    public static BitmapSource Load(string? path)
    {
        var image = path == null ? null : Read(path);
        return image != null && HasBootSize(image) ? image : Default();
    }

    public static bool HasBootSize(BitmapSource image) => image.PixelWidth == Size.Width && image.PixelHeight == Size.Height;

    public static SurfaceImage ToSurfaceImage(BitmapSource image)
    {
        var bgra = new FormatConvertedBitmap(image, PixelFormats.Bgra32, destinationPalette: null, alphaThreshold: 0);
        var pixels = new byte[bgra.PixelWidth * bgra.PixelHeight * 4];
        bgra.CopyPixels(pixels, bgra.PixelWidth * 4, offset: 0);
        return new SurfaceImage(bgra.PixelWidth, bgra.PixelHeight, pixels);
    }

    private static BitmapSource Decode(Stream stream)
    {
        var decoder = BitmapDecoder.Create(stream, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
        var frame = decoder.Frames[0];
        frame.Freeze();
        return frame;
    }
}
