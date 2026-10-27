using System.Collections.Concurrent;
using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using DockPad.Models;
using DockPad.Surfaces;

namespace DockPad.Services.StreamDock;

/// <summary>
/// Styles Néon et Aplat, pensés pour l'écran du dock : aplats et noir, sans dégradé, icône en grand.
/// </summary>
public static partial class DockImageRenderer
{
    private static readonly Color NeutralAccent = Color.FromRgb(150, 160, 175);
    private static readonly Color FlatNeutral = Color.FromRgb(52, 54, 60);
    private static readonly Brush NeonPressedBackground = FrozenBrush(Color.FromRgb(40, 40, 40));
    private static readonly Brush PressedShade = FrozenBrush(Color.FromArgb(46, 0, 0, 0));

    // Clé : chemin, date et taille du fichier ; une icône remplacée au même chemin est recalculée.
    private static readonly ConcurrentDictionary<string, Lazy<Color>> Accents = new(StringComparer.OrdinalIgnoreCase);

    private static SurfaceImage RenderStyledKey(DockKey key, DockKeyStyle style, bool pressed, int size)
    {
        var look = Look(key);
        if (look.Kind == LookKind.Empty)
            return Render(size, designSize: 96, Brushes.Black, _ => { });

        var background = style == DockKeyStyle.Flat
            ? FrozenBrush(look.Accent == null ? FlatNeutral : Mix(look.Accent.Value, Colors.Black, 0.55))
            : pressed ? NeonPressedBackground : Brushes.Black;
        return Render(size, designSize: 96, background, context =>
        {
            // Appuyée : le contenu recule ; en Aplat, la couleur s'assombrit en plus.
            if (pressed)
                context.PushTransform(new ScaleTransform(0.9, 0.9, 48, 48));
            if (style == DockKeyStyle.Flat)
                DrawFlat(context, look);
            else
                DrawNeon(context, look);
            if (pressed)
                context.Pop();
            if (pressed && style == DockKeyStyle.Flat)
                context.DrawRectangle(PressedShade, null, new Rect(0, 0, 96, 96));
        });
    }

    private static void DrawNeon(DrawingContext context, KeyLook look)
    {
        switch (look.Kind)
        {
            case LookKind.Page:
                DrawLookImage(context, look, new Rect(14, 6, 68, 68));
                break;
            case LookKind.Glyph:
                DrawGeometry(context, look.Glyph!, Brushes.White, new Rect(26, 12, 44, 44));
                break;
            case LookKind.Group:
                // Mosaïque plus petite et plus haute : le nom ne touche pas les icônes du bas.
                DrawLookImage(context, look, new Rect(24, 4, 48, 48));
                break;
            default:
                DrawLookImage(context, look, new Rect(19, 6, 58, 58));
                break;
        }
        if (look.Name != null)
            DrawText(context, look.Name, new Rect(3, 66, 90, 16), fontSize: 12);
        if (look.Accent != null)
            context.DrawRoundedRectangle(FrozenBrush(look.Accent.Value), null, new Rect(14, 86, 68, 3), 1.5, 1.5);
    }

    private static void DrawFlat(DrawingContext context, KeyLook look)
    {
        switch (look.Kind)
        {
            case LookKind.Page:
                DrawLookImage(context, look, new Rect(14, 12, 68, 68));
                break;
            case LookKind.Glyph:
                DrawGeometry(context, look.Glyph!, Brushes.White, new Rect(27, 13, 42, 42));
                break;
            case LookKind.Group:
                DrawLookImage(context, look, new Rect(26, 7, 44, 44));
                break;
            default:
                DrawLookImage(context, look, new Rect(21, 8, 54, 54));
                break;
        }
        if (look.Name != null)
            DrawText(context, look.Name, new Rect(3, 67, 90, 17), fontSize: 12);
    }

    private static void DrawLookImage(DrawingContext context, KeyLook look, Rect bounds)
    {
        switch (look.Kind)
        {
            case LookKind.Group:
                DrawGroupIcons(context, look.Group!, bounds);
                break;
            case LookKind.Initial:
                DrawText(context, look.Initial!, bounds, fontSize: bounds.Height * 0.6);
                break;
            case LookKind.Number:
                DrawText(context, look.Initial!, bounds, fontSize: 44);
                break;
            default:
                DrawImage(context, look.Icon!, bounds);
                break;
        }
    }

    /// <summary>
    /// Ce que montre une touche, indépendamment du style : image, nom et couleur d'accent (null : neutre).
    /// </summary>
    private static KeyLook Look(DockKey key)
    {
        switch (key)
        {
            case DockKey.Empty:
                return new KeyLook(LookKind.Empty);
            case DockKey.GoHome:
                return new KeyLook(LookKind.Glyph, Glyph: Home, Name: Loc.T("StreamDock_Home"));
            case DockKey.Back:
                return new KeyLook(LookKind.Glyph, Glyph: Left, Name: Loc.T("StreamDock_Back"));
            case DockKey.Next:
                return new KeyLook(LookKind.Glyph, Glyph: Right, Name: Loc.T("StreamDock_Next"));
            case DockKey.OpenGroup group:
                return new KeyLook(LookKind.Group, Group: group.Entry, Name: GroupName(group.Entry));
            case DockKey.OpenPage page:
            {
                var icon = LoadIcon(page.IconRef);
                return icon == null
                    ? new KeyLook(LookKind.Number, Initial: ((long)page.PageIndex + 1).ToString(Loc.Formatting))
                    : new KeyLook(LookKind.Page, Icon: icon, Accent: Accent(icon, page.IconRef));
            }
            case DockKey.RunTile tile:
            {
                var icon = LoadTileIcon(tile.Entry, out string? iconPath);
                if (icon != null)
                    return new KeyLook(LookKind.Icon, Icon: icon, Name: tile.Entry.Name, Accent: Accent(icon, iconPath));

                string initial = string.IsNullOrWhiteSpace(tile.Entry.Name) ? "?"
                    : StringInfo.GetNextTextElement(tile.Entry.Name.Trim()).ToUpper(Loc.Formatting);
                return new KeyLook(LookKind.Initial, Initial: initial, Name: tile.Entry.Name,
                    Accent: Vivid(InitialColor(tile.Entry.Name)));
            }
            default:
                throw new ArgumentOutOfRangeException(nameof(key), "Unknown dock key.");
        }
    }

    private static Color Accent(BitmapSource icon, string? iconPath)
    {
        string? cacheKey = AccentCacheKey(iconPath);
        return cacheKey == null
            ? Vivid(DominantColor(icon))
            : Accents.GetOrAdd(cacheKey, _ => new Lazy<Color>(() => Vivid(DominantColor(icon)))).Value;
    }

    private static string? AccentCacheKey(string? iconPath)
    {
        if (string.IsNullOrEmpty(iconPath))
            return null;
        try
        {
            var file = new FileInfo(iconPath);
            return file.Exists ? $"{file.FullName}|{file.LastWriteTimeUtc.Ticks}|{file.Length}" : null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            return null;
        }
    }

    /// <summary>
    /// Couleur la plus présente parmi les pixels opaques et colorés, regroupés par teinte voisine ; gris neutre sinon.
    /// </summary>
    internal static Color DominantColor(BitmapSource icon)
    {
        var small = new FormatConvertedBitmap(new TransformedBitmap(icon,
            new ScaleTransform(32d / icon.PixelWidth, 32d / icon.PixelHeight)), PixelFormats.Bgra32, null, 0);
        int width = small.PixelWidth, height = small.PixelHeight;
        var pixels = new byte[width * height * 4];
        small.CopyPixels(pixels, width * 4, 0);

        var buckets = new Dictionary<int, (double Weight, double Red, double Green, double Blue)>();
        for (int offset = 0; offset < pixels.Length; offset += 4)
        {
            if (pixels[offset + 3] < 128)
                continue;
            byte blue = pixels[offset], green = pixels[offset + 1], red = pixels[offset + 2];
            int chroma = Math.Max(red, Math.Max(green, blue)) - Math.Min(red, Math.Min(green, blue));
            if (chroma < 25)
                continue;
            int bucket = (red >> 5) << 6 | (green >> 5) << 3 | blue >> 5;
            double weight = chroma / 255d;
            buckets.TryGetValue(bucket, out var sum);
            buckets[bucket] = (sum.Weight + weight, sum.Red + red * weight, sum.Green + green * weight, sum.Blue + blue * weight);
        }
        if (buckets.Count == 0)
            return NeutralAccent;

        var main = buckets.Values.MaxBy(sum => sum.Weight);
        return Color.FromRgb((byte)Math.Round(main.Red / main.Weight), (byte)Math.Round(main.Green / main.Weight),
            (byte)Math.Round(main.Blue / main.Weight));
    }

    /// <summary>
    /// Même teinte poussée à pleine luminosité ; une couleur presque grise reste neutre.
    /// </summary>
    private static Color Vivid(Color color)
    {
        int max = Math.Max(color.R, Math.Max(color.G, color.B));
        int min = Math.Min(color.R, Math.Min(color.G, color.B));
        if (max - min < 25)
            return NeutralAccent;
        double scale = 255d / max;
        return Color.FromRgb((byte)Math.Round(color.R * scale), (byte)Math.Round(color.G * scale), (byte)Math.Round(color.B * scale));
    }

    private static Color Mix(Color color, Color target, double amount) => Color.FromRgb(
        (byte)Math.Round(color.R + (target.R - color.R) * amount),
        (byte)Math.Round(color.G + (target.G - color.G) * amount),
        (byte)Math.Round(color.B + (target.B - color.B) * amount));

    private enum LookKind { Empty, Icon, Initial, Glyph, Group, Page, Number }

    private sealed record KeyLook(LookKind Kind, BitmapSource? Icon = null, Geometry? Glyph = null,
        ShortcutEntry? Group = null, string? Initial = null, string? Name = null, Color? Accent = null);
}
