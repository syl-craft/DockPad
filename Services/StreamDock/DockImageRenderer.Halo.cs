using System.Collections.Concurrent;
using System.Globalization;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using DockPad.Models;
using DockPad.Surfaces;

namespace DockPad.Services.StreamDock;

/// <summary>
/// Style Halo : tuile arrondie teintée par l'icône, halo et reflet, nom en bas dans la tuile.
/// </summary>
public static partial class DockImageRenderer
{
    private const double HaloRadius = 13;
    private static readonly Rect HaloTile = new(4, 4, 88, 88);
    private static readonly Rect HaloIcon = new(25, 15, 46, 46);
    private static readonly Rect HaloName = new(7, 71, 82, 17);
    private static readonly Color HaloNeutral = Color.FromRgb(90, 90, 100);
    private static readonly Brush HaloNameShadow = FrozenBrush(Color.FromArgb(150, 0, 0, 0));
    private static readonly Brush HaloPressedShade = FrozenBrush(Color.FromArgb(46, 0, 0, 0));
    private static readonly Pen HaloEdge = FrozenPen(Color.FromArgb(36, 255, 255, 255));
    private static readonly Brush HaloSheen = FrozenLinear(new Point(0.37, 0), new Point(0.63, 1),
        (Color.FromArgb(77, 255, 255, 255), 0), (Color.FromArgb(15, 255, 255, 255), 0.38), (Color.FromArgb(0, 255, 255, 255), 0.52));
    private static readonly Brush HaloGlass = FrozenLinear(new Point(0.35, 0), new Point(0.65, 1),
        (Color.FromRgb(62, 62, 66), 0), (Color.FromRgb(26, 26, 28), 1));

    // Les icônes du profil portent un nom tiré de leur contenu : un même chemin garde la même teinte.
    private static readonly ConcurrentDictionary<string, Color> HaloTints = new(StringComparer.OrdinalIgnoreCase);

    private static SurfaceImage RenderHaloKey(DockKey key, bool pressed, int size) =>
        Render(size, designSize: 96, Brushes.Black, context =>
        {
            if (key is DockKey.Empty)
                return;

            // Appuyée : la tuile entière recule, puis s'assombrit.
            if (pressed)
                context.PushTransform(new ScaleTransform(0.9, 0.9, 48, 48));
            switch (key)
            {
                case DockKey.GoHome:
                    DrawHaloNavigation(context, Home, Loc.T("StreamDock_Home"));
                    break;
                case DockKey.Back:
                    DrawHaloNavigation(context, Left, Loc.T("StreamDock_Back"));
                    break;
                case DockKey.Next:
                    DrawHaloNavigation(context, Right, Loc.T("StreamDock_Next"));
                    break;
                case DockKey.OpenPage page:
                    DrawHaloPage(context, page);
                    break;
                case DockKey.RunTile tile:
                    DrawHaloTile(context, tile.Entry);
                    break;
                case DockKey.OpenGroup group:
                    DrawGlassTile(context);
                    DrawGroupIcons(context, group.Entry, new Rect(27, 12, 42, 42));
                    DrawHaloName(context, GroupName(group.Entry));
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(key), "Unknown dock key.");
            }
            if (pressed)
            {
                context.DrawRoundedRectangle(HaloPressedShade, null, HaloTile, HaloRadius, HaloRadius);
                context.Pop();
            }
        });

    private static void DrawHaloTile(DrawingContext context, ShortcutEntry entry)
    {
        var icon = LoadTileIcon(entry, out string? iconPath);
        if (icon == null)
        {
            DrawTintedTile(context, InitialColor(entry.Name));
            string initial = string.IsNullOrWhiteSpace(entry.Name) ? "?"
                : StringInfo.GetNextTextElement(entry.Name.Trim()).ToUpper(Loc.Formatting);
            DrawText(context, initial, HaloIcon, fontSize: 30);
        }
        else
        {
            DrawTintedTile(context, Tint(icon, iconPath));
            DrawImage(context, icon, HaloIcon);
        }
        DrawHaloName(context, entry.Name);
    }

    /// <summary>
    /// Page de l'accueil : son icône en grand, sans nom ; le numéro sur verre faute d'icône.
    /// </summary>
    private static void DrawHaloPage(DrawingContext context, DockKey.OpenPage page)
    {
        var icon = LoadIcon(page.IconRef);
        if (icon == null)
        {
            DrawGlassTile(context);
            string number = ((long)page.PageIndex + 1).ToString(Loc.Formatting);
            DrawText(context, number, HaloTile, fontSize: 40);
            return;
        }

        DrawTintedTile(context, Tint(icon, page.IconRef));
        DrawImage(context, icon, new Rect(18, 18, 60, 60));
    }

    private static void DrawHaloNavigation(DrawingContext context, Geometry glyph, string label)
    {
        DrawGlassTile(context);
        DrawGeometry(context, glyph, Brushes.White, new Rect(31, 19, 34, 34));
        DrawHaloName(context, label);
    }

    private static void DrawHaloName(DrawingContext context, string name)
    {
        DrawText(context, name, new Rect(HaloName.X, HaloName.Y + 1, HaloName.Width, HaloName.Height), fontSize: 11,
            fill: HaloNameShadow);
        DrawText(context, name, HaloName, fontSize: 11);
    }

    /// <summary>
    /// Dégradé foncé de la teinte, halo clair derrière l'icône, reflet en haut et liseré.
    /// </summary>
    private static void DrawTintedTile(DrawingContext context, Color tint)
    {
        var body = FrozenLinear(new Point(0.41, 0), new Point(0.59, 1),
            (Mix(tint, Colors.Black, 0.45), 0), (Mix(tint, Colors.Black, 0.62), 0.55), (Mix(tint, Colors.Black, 0.80), 1));
        context.DrawRoundedRectangle(body, null, HaloTile, HaloRadius, HaloRadius);

        var glowColor = Mix(tint, Colors.White, 0.10);
        var glow = new RadialGradientBrush
        {
            Center = new Point(0.5, 0.46),
            GradientOrigin = new Point(0.5, 0.46),
            RadiusX = 0.58,
            RadiusY = 0.58,
        };
        glow.GradientStops.Add(new GradientStop(Color.FromArgb(217, glowColor.R, glowColor.G, glowColor.B), 0));
        glow.GradientStops.Add(new GradientStop(Color.FromArgb(0, glowColor.R, glowColor.G, glowColor.B), 1));
        glow.Freeze();
        context.DrawRoundedRectangle(glow, null, HaloTile, HaloRadius, HaloRadius);
        context.DrawRoundedRectangle(HaloSheen, HaloEdge, Inset(HaloTile), HaloRadius, HaloRadius);
    }

    private static void DrawGlassTile(DrawingContext context)
    {
        context.DrawRoundedRectangle(HaloGlass, null, HaloTile, HaloRadius, HaloRadius);
        context.DrawRoundedRectangle(HaloSheen, HaloEdge, Inset(HaloTile), HaloRadius, HaloRadius);
    }

    private static Color Tint(BitmapSource icon, string? iconPath) => iconPath == null
        ? DominantColor(icon)
        : HaloTints.GetOrAdd(iconPath, _ => DominantColor(icon));

    /// <summary>
    /// Moyenne des pixels opaques, les plus saturés pesant davantage ; gris neutre pour une icône transparente.
    /// </summary>
    internal static Color DominantColor(BitmapSource icon)
    {
        var small = new FormatConvertedBitmap(new TransformedBitmap(icon,
            new ScaleTransform(32d / icon.PixelWidth, 32d / icon.PixelHeight)), PixelFormats.Bgra32, null, 0);
        int width = small.PixelWidth, height = small.PixelHeight;
        var pixels = new byte[width * height * 4];
        small.CopyPixels(pixels, width * 4, 0);

        double red = 0, green = 0, blue = 0, weights = 0;
        for (int offset = 0; offset < pixels.Length; offset += 4)
        {
            if (pixels[offset + 3] < 128)
                continue;
            byte b = pixels[offset], g = pixels[offset + 1], r = pixels[offset + 2];
            double weight = 1 + (Math.Max(r, Math.Max(g, b)) - Math.Min(r, Math.Min(g, b))) / 40d;
            red += r * weight;
            green += g * weight;
            blue += b * weight;
            weights += weight;
        }
        return weights == 0 ? HaloNeutral
            : Color.FromRgb((byte)Math.Round(red / weights), (byte)Math.Round(green / weights), (byte)Math.Round(blue / weights));
    }

    private static Color Mix(Color color, Color target, double amount) => Color.FromRgb(
        (byte)Math.Round(color.R + (target.R - color.R) * amount),
        (byte)Math.Round(color.G + (target.G - color.G) * amount),
        (byte)Math.Round(color.B + (target.B - color.B) * amount));

    private static Rect Inset(Rect rect) => new(rect.X + 0.5, rect.Y + 0.5, rect.Width - 1, rect.Height - 1);

    private static Brush FrozenLinear(Point start, Point end, params (Color Color, double Offset)[] stops)
    {
        var brush = new LinearGradientBrush { StartPoint = start, EndPoint = end };
        foreach (var (color, offset) in stops)
            brush.GradientStops.Add(new GradientStop(color, offset));
        brush.Freeze();
        return brush;
    }

    private static Pen FrozenPen(Color color)
    {
        var pen = new Pen(FrozenBrush(color), 1);
        pen.Freeze();
        return pen;
    }
}
