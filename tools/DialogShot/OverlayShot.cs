using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using DockPad;

namespace DialogShot;

/// <summary>
/// Rend l'overlay des raccourcis clavier, deployé, dans un PNG.
/// </summary>
/// <remarks>
/// L'overlay a changé de conteneur : ses éléments vivaient dans la grille des tuiles, ils ont
/// maintenant leur propre grille superposée. Les fenêtres au repos ne montrent rien de ce
/// changement — d'où cette cible, comparable d'une version à l'autre parce qu'elle appelle
/// <c>ShowHintOverlay</c> par réflexion, sous un nom que les deux versions portent.
/// </remarks>
internal static class OverlayShot
{
    public static void Render(string path, bool firstHalf)
    {
        GridCheck.WriteFixture();

        var window = new QuickAccessWindow();
        var content = (FrameworkElement)window.Content;
        BindingCheck.ForceLayout(content, window.Width);

        typeof(QuickAccessWindow)
            .GetMethod("ShowHintOverlay", BindingFlags.NonPublic | BindingFlags.Instance)!
            .Invoke(window, [firstHalf]);

        BindingCheck.ForceLayout(content, window.Width);

        var bitmap = new RenderTargetBitmap(
            (int)Math.Ceiling(content.ActualWidth), (int)Math.Ceiling(content.ActualHeight),
            96, 96, PixelFormats.Pbgra32);
        bitmap.Render(content);

        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var stream = File.Create(path);
        encoder.Save(stream);

        Console.WriteLine($"  overlay rendu : {path} ({bitmap.PixelWidth}x{bitmap.PixelHeight})");
    }
}
