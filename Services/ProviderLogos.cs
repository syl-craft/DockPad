using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace DockPad.Services;

/// <summary>
/// Les logos de fournisseurs dont le tracé sert à la fois au bandeau Usage IA et à l'icône d'un
/// prédéfini.
/// </summary>
/// <remarks>
/// <para>
/// Ceux de Claude et de Codex vivent dans <c>Views/ProviderIcon.xaml</c> : le bandeau est leur
/// seul usage, et leurs prédéfinis tirent l'icône d'une application installée. Antigravity n'en a
/// aucune — <c>agy.exe</c> n'embarque pas d'icône — donc le menu de Windows prend le même tracé
/// que le bandeau, et il doit n'exister qu'à un endroit.
/// </para>
/// <para>Tracé Lobe Icons (MIT), sources et licence dans <c>Assets/ProviderLogos.LICENSE.txt</c>.</para>
/// </remarks>
public static class ProviderLogos
{
    private const string AntigravityPath =
        "M21.751 22.607c1.34 1.005 3.35.335 1.508-1.508C17.73 15.74 18.904 1 12.037 1 5.17 1 6.342 15.74.815 21.1"
        + "c-2.01 2.009.167 2.511 1.507 1.506 5.192-3.517 4.857-9.714 9.715-9.714 4.857 0 4.522 6.197 9.714 9.715z";

    /// <summary>Le bleu d'Antigravity, déjà celui de sa pastille : lisible sur les deux thèmes.</summary>
    public const string AntigravityAccent = "#1A73E8";

    public static Geometry Antigravity { get; } = Frozen(Geometry.Parse(AntigravityPath));

    public static Brush AntigravityBrush { get; } =
        Frozen(new SolidColorBrush((Color)ColorConverter.ConvertFromString(AntigravityAccent)));

    /// <summary>
    /// Le tracé rempli de sa couleur, centré sur un carré transparent de <paramref name="size"/>
    /// pixels, encodé en PNG.
    /// </summary>
    public static byte[] RenderPng(Geometry logo, Brush fill, int size)
    {
        var visual = new DrawingVisual();
        using (var context = visual.RenderOpen())
        {
            // Le tracé ne remplit pas son carré de 24 : on le centre à sa propre échelle, comme le
            // fait le Path en Stretch="Uniform" du bandeau.
            var bounds = logo.Bounds;
            var scale = size / Math.Max(bounds.Width, bounds.Height);
            context.PushTransform(new TranslateTransform(
                (size - bounds.Width * scale) / 2 - bounds.X * scale,
                (size - bounds.Height * scale) / 2 - bounds.Y * scale));
            context.PushTransform(new ScaleTransform(scale, scale));
            context.DrawGeometry(fill, null, logo);
        }

        var bitmap = new RenderTargetBitmap(size, size, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(visual);

        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var png = new MemoryStream();
        encoder.Save(png);
        return png.ToArray();
    }

    private static T Frozen<T>(T freezable) where T : Freezable
    {
        freezable.Freeze();
        return freezable;
    }
}
