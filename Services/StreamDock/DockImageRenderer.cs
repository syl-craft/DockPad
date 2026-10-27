using System.Globalization;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using DockPad.Models;
using DockPad.Services.Usage;
using DockPad.Surfaces;
using DockPad.Views;

namespace DockPad.Services.StreamDock;

/// <summary>
/// Images droites et opaques destinées à l’encodage JPEG du pilote, à rendre depuis un thread STA.
/// </summary>
public static class DockImageRenderer
{
    private static readonly Brush KeyBackground = FrozenBrush(Color.FromRgb(30, 30, 30));
    private static readonly Brush PressedBackground = FrozenBrush(Color.FromRgb(58, 58, 58));
    private static readonly Brush Muted = FrozenBrush(Color.FromRgb(175, 175, 175));
    private static readonly Brush Stale = FrozenBrush(Color.FromRgb(145, 145, 145));
    private static readonly Brush GaugeTrack = FrozenBrush(Color.FromRgb(55, 55, 55));
    private static readonly Typeface Typeface = new(new FontFamily("Segoe UI"), FontStyles.Normal,
        FontWeights.SemiBold, FontStretches.Normal);
    private static readonly Geometry Home = FrozenGeometry("M 2,11 L 12,2 L 22,11 L 19,11 L 19,22 L 14,22 L 14,15 L 10,15 L 10,22 L 5,22 L 5,11 Z");
    private static readonly Geometry Left = FrozenGeometry("M 13,2 L 3,12 L 13,22 L 16,19 L 11,14 L 23,14 L 23,10 L 11,10 L 16,5 Z");
    private static readonly Geometry Right = FrozenGeometry("M 11,2 L 21,12 L 11,22 L 8,19 L 13,14 L 1,14 L 1,10 L 13,10 L 8,5 Z");

    public static SurfaceImage RenderKey(DockKey key, bool pressed, int size)
    {
        ArgumentNullException.ThrowIfNull(key);
        return Render(size, designSize: 96, key is DockKey.Empty ? Brushes.Black
            : pressed ? PressedBackground : KeyBackground, context =>
        {
            if (key is DockKey.Empty)
                return;

            var iconBounds = new Rect(24, 8, 48, 48);
            if (pressed)
                iconBounds = new Rect(iconBounds.X + iconBounds.Width * 0.075,
                    iconBounds.Y + iconBounds.Height * 0.075, iconBounds.Width * 0.85, iconBounds.Height * 0.85);

            switch (key)
            {
                case DockKey.GoHome:
                    DrawNavigation(context, Home, Loc.T("StreamDock_Home"), iconBounds);
                    break;
                case DockKey.Back:
                    DrawNavigation(context, Left, Loc.T("StreamDock_Back"), iconBounds);
                    break;
                case DockKey.Next:
                    DrawNavigation(context, Right, Loc.T("StreamDock_Next"), iconBounds);
                    break;
                case DockKey.OpenPage page:
                    DrawPage(context, page, pressed);
                    break;
                case DockKey.RunTile tile:
                    DrawTile(context, tile.Entry, iconBounds);
                    break;
                case DockKey.OpenGroup group:
                    DrawGroup(context, group.Entry, iconBounds);
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(key), "Unknown dock key.");
            }
        });
    }

    public static SurfaceImage RenderDisplay(UsageDisplay? display, int size) =>
        Render(size, designSize: 80, display == null ? Brushes.Black : KeyBackground, context =>
        {
            switch (display)
            {
                case null:
                    return;
                case UsageDisplay.Gauge gauge:
                    DrawGauge(context, gauge);
                    break;
                case UsageDisplay.Identity identity:
                    DrawIdentity(context, identity);
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(display), "Unknown usage display.");
            }
        });

    private static void DrawNavigation(DrawingContext context, Geometry icon, string label, Rect bounds)
    {
        DrawGeometry(context, icon, Brushes.White, bounds);
        DrawKeyLabel(context, label);
    }

    /// <summary>
    /// Icône de la page sur toute la touche, sans libellé ; le numéro seulement faute d'icône.
    /// </summary>
    private static void DrawPage(DrawingContext context, DockKey.OpenPage page, bool pressed)
    {
        var bounds = pressed ? new Rect(17.4, 17.4, 61.2, 61.2) : new Rect(12, 12, 72, 72);
        var icon = LoadIcon(page.IconRef);
        if (icon == null)
        {
            string number = ((long)page.PageIndex + 1).ToString(Loc.Formatting);
            DrawText(context, number, bounds, fontSize: pressed ? 44 * 0.85 : 44);
            return;
        }

        DrawImage(context, icon, bounds);
    }

    private static void DrawTile(DrawingContext context, ShortcutEntry entry, Rect bounds)
    {
        DrawTileIcon(context, entry, bounds);
        DrawKeyLabel(context, entry.Name);
    }

    private static void DrawGroup(DrawingContext context, ShortcutEntry group, Rect bounds)
    {
        int capacity = TileGroupService.Capacity(group.Layout);
        double gap = bounds.Width / 24;
        double iconScale = group.Layout == TileLayout.TwoPlusFour
            ? (bounds.Width / 4 - gap) / 18
            : (bounds.Width / 2 - gap) / 24;
        for (int slot = 0; slot < capacity; slot++)
        {
            var child = group.Children?.ElementAtOrDefault(slot);
            if (child == null)
                continue;

            var cell = TileGroupPanel.Bounds(group.Layout, slot, bounds.Size);
            double iconSize = group.Layout == TileLayout.TwoPlusFour && slot >= 2 ? 18 : 24;
            double side = iconSize * iconScale;
            var iconBounds = new Rect(bounds.X + cell.X + (cell.Width - side) / 2,
                bounds.Y + cell.Y + (cell.Height - side) / 2, side, side);
            DrawTileIcon(context, child, iconBounds);
        }

        string name = string.IsNullOrWhiteSpace(group.Name) ? Loc.T("Group_Actions") : group.Name;
        DrawKeyLabel(context, name);
    }

    private static void DrawKeyLabel(DrawingContext context, string label) =>
        DrawText(context, label, new Rect(4, 60, 88, 32), fontSize: 13, lineCount: 2);

    private static void DrawGauge(DrawingContext context, UsageDisplay.Gauge gauge)
    {
        Brush foreground = gauge.IsStale ? Stale : Brushes.White;
        DrawProvider(context, gauge.ProviderId, new Rect(30, 5, 20, 20), gauge.IsStale);
        DrawText(context, gauge.UsedPct.ToString(Loc.Formatting) + "%",
            new Rect(4, 27, 72, 29), fontSize: 23, fill: foreground);
        DrawText(context, gauge.Label, new Rect(4, 56, 72, 14), fontSize: 10,
            fill: gauge.IsStale ? Stale : Muted);
        var track = new Rect(6, 73, 68, 4);
        context.DrawRoundedRectangle(GaugeTrack, null, track, 2, 2);
        double width = track.Width * Math.Clamp(gauge.UsedPct, 0, 100) / 100d;
        if (width > 0)
            context.DrawRoundedRectangle(gauge.IsStale ? Stale : ResolveGaugeBrush(gauge.Color), null,
                new Rect(track.X, track.Y, width, track.Height), 2, 2);
    }

    private static Brush ResolveGaugeBrush(string? color)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(color))
                throw new FormatException("Gauge color is missing.");

            var converted = ColorConverter.ConvertFromString(color);
            if (converted is Color gaugeColor)
                return FrozenBrush(gaugeColor);

            throw new FormatException("Gauge color is invalid.");
        }
        catch (Exception ex)
        {
            LogService.Warn(ex, "Couleur de jauge Stream Dock invalide : utilisation de la couleur atténuée.");
            return Muted;
        }
    }

    private static void DrawIdentity(DrawingContext context, UsageDisplay.Identity identity)
    {
        DrawProvider(context, identity.ProviderId, new Rect(27, 5, 26, 26), stale: false);
        DrawText(context, identity.Name, new Rect(4, 34, 72, 28), fontSize: 11, lineCount: 2);
        DrawText(context, UsageFormat.Reset(identity.NextReset, DateTime.Now),
            new Rect(4, 63, 72, 13), fontSize: 10, fill: Muted);
    }

    private static SurfaceImage Render(int size, double designSize, Brush background, Action<DrawingContext> draw)
    {
        if (size <= 0)
            throw new ArgumentOutOfRangeException(nameof(size), "Image size must be positive.");

        var visual = new DrawingVisual();
        TextOptions.SetTextRenderingMode(visual, TextRenderingMode.Grayscale);
        RenderOptions.SetBitmapScalingMode(visual, BitmapScalingMode.HighQuality);
        using (var context = visual.RenderOpen())
        {
            // Le fond couvre aussi la marge : aucun pixel transparent ne doit partir vers le JPEG 24 bits.
            context.DrawRectangle(background, null, new Rect(0, 0, size, size));
            context.PushTransform(new ScaleTransform(size / designSize, size / designSize));
            context.PushClip(new RectangleGeometry(new Rect(3, 3, designSize - 6, designSize - 6)));
            draw(context);
            context.Pop();
            context.Pop();
        }

        var bitmap = new RenderTargetBitmap(size, size, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(visual);
        var pixels = new byte[checked(size * size * 4)];
        bitmap.CopyPixels(pixels, checked(size * 4), 0);
        // Sur un fond opaque, les composantes prémultipliées Pbgra32 sont identiques aux composantes BGRA.
        return new SurfaceImage(size, size, pixels);
    }

    private static void DrawTileIcon(DrawingContext context, ShortcutEntry entry, Rect bounds)
    {
        BitmapSource? icon;
        try
        {
            icon = IconStoreService.LoadImage(
                IconStoreService.ResolveProfilePath(entry.IconProfilePath) ?? entry.IconPath);
        }
        catch (Exception ex)
        {
            LogService.Warn(ex, "Impossible de charger une icône de tuile du Stream Dock.");
            icon = null;
        }

        if (icon == null)
            DrawInitial(context, entry.Name, bounds, InitialBackground(entry.Name), Brushes.White);
        else
            DrawImage(context, icon, bounds);
    }

    private static BitmapSource? LoadIcon(string? iconRef)
    {
        try { return IconStoreService.LoadImage(iconRef); }
        catch (Exception ex)
        {
            LogService.Warn(ex, "Impossible de charger une icône de page du Stream Dock.");
            return null;
        }
    }

    private static void DrawProvider(DrawingContext context, string providerId, Rect bounds, bool stale)
    {
        if (string.Equals(providerId, "antigravity", StringComparison.OrdinalIgnoreCase))
            DrawGeometry(context, ProviderLogos.Antigravity, stale ? Stale : ProviderLogos.AntigravityBrush, bounds);
        else
            DrawInitial(context, providerId, bounds, stale ? GaugeTrack : InitialBackground(providerId),
                stale ? Stale : Brushes.White);
    }

    private static void DrawInitial(DrawingContext context, string name, Rect bounds, Brush background, Brush foreground)
    {
        context.DrawRoundedRectangle(background, null, bounds, bounds.Width / 6, bounds.Height / 6);
        string initial = string.IsNullOrWhiteSpace(name) ? "?" : StringInfo.GetNextTextElement(name.Trim()).ToUpper(Loc.Formatting);
        DrawText(context, initial, bounds, fontSize: bounds.Height * 0.58, fill: foreground);
    }

    private static Brush InitialBackground(string name)
    {
        uint hash = 2166136261;
        foreach (char character in name)
            hash = unchecked((hash ^ character) * 16777619);
        return FrozenBrush(Color.FromRgb((byte)(55 + hash % 90),
            (byte)(55 + (hash >> 8) % 90), (byte)(55 + (hash >> 16) % 90)));
    }

    private static void DrawImage(DrawingContext context, BitmapSource image, Rect bounds)
    {
        double scale = Math.Min(bounds.Width / image.Width, bounds.Height / image.Height);
        double width = image.Width * scale;
        double height = image.Height * scale;
        context.DrawImage(image, new Rect(bounds.X + (bounds.Width - width) / 2,
            bounds.Y + (bounds.Height - height) / 2, width, height));
    }

    private static void DrawGeometry(DrawingContext context, Geometry geometry, Brush fill, Rect bounds)
    {
        var source = geometry.Bounds;
        double scale = Math.Min(bounds.Width / source.Width, bounds.Height / source.Height);
        context.PushTransform(new TranslateTransform(bounds.X + (bounds.Width - source.Width * scale) / 2 - source.X * scale,
            bounds.Y + (bounds.Height - source.Height * scale) / 2 - source.Y * scale));
        context.PushTransform(new ScaleTransform(scale, scale));
        context.DrawGeometry(fill, null, geometry);
        context.Pop();
        context.Pop();
    }

    private static void DrawText(DrawingContext context, string text, Rect bounds, double fontSize,
        int lineCount = 1, Brush? fill = null)
    {
        if (string.IsNullOrWhiteSpace(text))
            return;

        var formatted = new FormattedText(text, Loc.Formatting, FlowDirection.LeftToRight,
            Typeface, fontSize, fill ?? Brushes.White, pixelsPerDip: 1)
        {
            MaxTextWidth = bounds.Width,
            MaxTextHeight = bounds.Height,
            MaxLineCount = lineCount,
            LineHeight = lineCount == 2 ? fontSize * 16 / 13 : fontSize * 1.2,
            TextAlignment = TextAlignment.Center,
            Trimming = TextTrimming.CharacterEllipsis,
        };
        context.PushClip(new RectangleGeometry(bounds));
        context.DrawText(formatted, new Point(bounds.X,
            bounds.Y + (lineCount == 1 ? Math.Max(0, (bounds.Height - formatted.Height) / 2) : 0)));
        context.Pop();
    }

    private static Brush FrozenBrush(Color color)
    {
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        return brush;
    }

    private static Geometry FrozenGeometry(string path)
    {
        var geometry = Geometry.Parse(path);
        geometry.Freeze();
        return geometry;
    }
}
