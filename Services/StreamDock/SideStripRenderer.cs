using System.Globalization;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using DockPad.Models;
using DockPad.Services.Usage;
using DockPad.Surfaces;

namespace DockPad.Services.StreamDock;

/// <summary>
/// Colonne opaque en pixels fixes, à dessiner sur un thread STA sans accès au matériel ni à l'horloge.
/// </summary>
public static class SideStripRenderer
{
    private static readonly Brush Background = FrozenBrush(0, 0, 0);
    private static readonly Brush Track = FrozenBrush(40, 43, 50);
    private static readonly Brush Foreground = FrozenBrush(238, 240, 244);
    private static readonly Brush Muted = FrozenBrush(166, 172, 184);
    private static readonly Brush Stale = FrozenBrush(145, 145, 145);
    private static readonly Brush Claude = ProviderLogos.ClaudeBrush;
    // Le logo de Codex est noir dans le bandeau : un gris clair le garde lisible sur le fond noir.
    private static readonly Brush Codex = FrozenBrush(208, 212, 220);
    private static readonly Brush Antigravity = ProviderLogos.AntigravityBrush;
    private static readonly Brush Neutral = FrozenBrush(212, 212, 212);
    private static readonly Brush Cpu = FrozenBrush(242, 193, 78);
    private static readonly Brush Memory = FrozenBrush(181, 140, 242);
    private static readonly Brush Alert = CreateAlertBrush();
    private static readonly Typeface Typeface = new(new FontFamily("Consolas"), FontStyles.Normal,
        FontWeights.Bold, FontStretches.Normal);

    private sealed record Meter(string Label, string ProviderId, string ProviderName, int? Percent,
        Brush Color, bool IsStale = false, DateTime? ResetsAt = null, UsageWindowKind? WindowKind = null,
        TimeSpan? ResetIn = null);

    internal sealed record DrawnText(string SourceText, string Text, Rect Zone, Size MeasuredSize, double FontSize);

    // Le collecteur reste sur le thread STA du rendu qui l'a installé.
    [ThreadStatic]
    internal static Action<DrawnText>? TextDrawn;

    public static SurfaceImage Render(SideStripContent content, SideStripStyle style, int width, int height)
    {
        ArgumentNullException.ThrowIfNull(content);
        if (width <= 0)
            throw new ArgumentOutOfRangeException(nameof(width), "Image width must be positive.");
        if (height <= 0)
            throw new ArgumentOutOfRangeException(nameof(height), "Image height must be positive.");

        var visual = new DrawingVisual();
        TextOptions.SetTextRenderingMode(visual, TextRenderingMode.Grayscale);
        using (var context = visual.RenderOpen())
        {
            context.DrawRectangle(Background, null, new Rect(0, 0, width, height));
            double margin = Math.Min(width, height) * 0.04;
            var bounds = new Rect(margin, margin, width - margin * 2, height - margin * 2);
            context.PushClip(new RectangleGeometry(bounds));
            var quotas = content.Quotas.Select(ToMeter).ToArray();
            Meter[] pc = [new(Loc.T("StreamDock_Cpu"), "", Loc.T("StreamDock_Cpu"), content.CpuPct, Cpu),
                new(Loc.T("StreamDock_Memory"), "", Loc.T("StreamDock_Memory"), content.MemoryPct, Memory)];
            switch (style)
            {
                case SideStripStyle.HeroStack:
                    DrawHeroStack(context, bounds, quotas, pc);
                    break;
                case SideStripStyle.Rings:
                    DrawRings(context, bounds, quotas, pc);
                    break;
                case SideStripStyle.Reservoir:
                    DrawReservoir(context, bounds, quotas, pc);
                    break;
                default:
                    DrawColumns(context, bounds, GroupedByProvider(quotas.Take(3)), pc);
                    break;
            }
            context.Pop();
        }

        var bitmap = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(visual);
        var pixels = new byte[checked(width * height * 4)];
        bitmap.CopyPixels(pixels, checked(width * 4), 0);
        return new SurfaceImage(width, height, pixels);
    }

    /// <summary>
    /// Même police et même densité que le dessin ; la troncature conserve les éléments Unicode entiers.
    /// </summary>
    internal static (string Text, double FontSize) Fit(string text, double maxWidth, double fontSize, double minFontSize)
    {
        ArgumentNullException.ThrowIfNull(text);
        if (double.IsFinite(maxWidth) == false || maxWidth < 0)
            throw new ArgumentOutOfRangeException(nameof(maxWidth), "Text width must be finite and non-negative.");
        if (double.IsFinite(minFontSize) == false || minFontSize <= 0)
            throw new ArgumentOutOfRangeException(nameof(minFontSize), "Minimum font size must be finite and positive.");
        if (double.IsFinite(fontSize) == false || fontSize < minFontSize)
            throw new ArgumentOutOfRangeException(nameof(fontSize), "Font size must be finite and at least the minimum.");

        text = text.Replace('\r', ' ').Replace('\n', ' ').Replace('\t', ' ');
        double size = fontSize;
        while (size > minFontSize && Measure(text, size).WidthIncludingTrailingWhitespace > maxWidth)
            size = Math.Max(minFontSize, size - 0.5);
        if (Measure(text, size).WidthIncludingTrailingWhitespace <= maxWidth)
            return (text, size);
        if (Measure("…", size).WidthIncludingTrailingWhitespace > maxWidth)
            return ("", size);

        int[] elements = StringInfo.ParseCombiningCharacters(text);
        int low = 0;
        int high = elements.Length;
        while (low < high)
        {
            int middle = (low + high + 1) / 2;
            int length = middle == elements.Length ? text.Length : elements[middle];
            if (Measure(text[..length] + "…", size).WidthIncludingTrailingWhitespace <= maxWidth)
                low = middle;
            else
                high = middle - 1;
        }
        int end = low == elements.Length ? text.Length : elements[low];
        return (text[..end] + "…", size);
    }

    private static Meter ToMeter(QuotaGauge quota) => new(quota.WindowLabel, quota.ProviderId,
        quota.ProviderName, quota.UsedPct, quota.IsStale ? Stale : quota.IsAlert ? Alert : IdentityColor(quota.ProviderId),
        quota.IsStale, quota.ResetsAt, quota.WindowKind, quota.ResetIn);

    private static Brush IdentityColor(string providerId) => providerId.ToLowerInvariant() switch
    {
        "claude" => Claude,
        "codex" => Codex,
        "antigravity" => Antigravity,
        _ => Neutral,
    };

    private static Meter Hero(IReadOnlyList<Meter> quotas, Meter[] pc) =>
        quotas.Count > 0 ? quotas[0] : pc.OrderByDescending(meter => meter.Percent ?? -1).First();

    /// <summary>
    /// Les fenêtres d'un même fournisseur restent côte à côte, sous un seul logo.
    /// </summary>
    private static Meter[] GroupedByProvider(IEnumerable<Meter> quotas) => quotas
        .GroupBy(quota => quota.ProviderId, StringComparer.OrdinalIgnoreCase)
        .SelectMany(group => group)
        .ToArray();

    private static void DrawColumns(DrawingContext context, Rect bounds, Meter[] quotas, Meter[] pc)
    {
        var meters = quotas.Concat(pc).ToArray();
        double cellWidth = bounds.Width / meters.Length;
        double gap = Math.Max(2, bounds.Width * 0.045);
        double valueSize = Font(bounds, 0.18);
        bool stagger = Measure("100", valueSize).WidthIncludingTrailingWhitespace + gap > cellWidth;
        for (int index = 0; index < meters.Length; index++)
        {
            var meter = meters[index];
            var cell = new Rect(bounds.X + index * cellWidth, bounds.Y, cellWidth, bounds.Height);
            double center = cell.X + cellWidth / 2;
            double textWidth = cellWidth * (stagger ? 2 : 1) - gap;
            double left = Math.Max(bounds.Left, center - textWidth / 2);
            double right = Math.Min(bounds.Right, center + textWidth / 2);
            double row = stagger ? index % 2 : 0;
            var value = new Rect(left, bounds.Y + bounds.Height * (0.01 + row * 0.07), right - left,
                bounds.Height * (stagger ? 0.06 : 0.13));
            DrawText(context, Percent(meter.Percent, withSymbol: false), value, valueSize, meter.Color);
            var track = Part(cell, 0.24, 0.16, 0.52, 0.64);
            var clip = new RectangleGeometry(track, track.Width / 2, track.Width / 2);
            context.DrawGeometry(Track, null, clip);
            context.PushClip(clip);
            double fillHeight = track.Height * Fraction(meter.Percent);
            context.DrawRectangle(meter.Color, null, new Rect(track.X, track.Bottom - fillHeight, track.Width, fillHeight));
            context.Pop();
            if (index < quotas.Length)
                DrawText(context, ShortLabel(meter), new Rect(cell.X + 1, bounds.Y + bounds.Height * 0.815,
                    cellWidth - 2, bounds.Height * 0.06), Font(bounds, 0.13), meter.IsStale ? Stale : Muted);
        }

        var iconRow = Part(bounds, 0, 0.885, 1, 0.115);
        int first = 0;
        foreach (var group in quotas.GroupBy(quota => quota.ProviderId, StringComparer.OrdinalIgnoreCase))
        {
            var logo = Icon(iconRow, bounds.X + first * cellWidth, cellWidth * group.Count());
            DrawProvider(context, group.First() with { IsStale = group.All(meter => meter.IsStale) }, logo);
            first += group.Count();
        }
        DrawCpuIcon(context, Icon(iconRow, bounds.X + quotas.Length * cellWidth, cellWidth), pc[0].Color);
        DrawMemoryIcon(context, Icon(iconRow, bounds.X + (quotas.Length + 1) * cellWidth, cellWidth), pc[1].Color);
    }

    private static Rect Icon(Rect row, double left, double width)
    {
        double side = Math.Min(width - 2, row.Height);
        return new Rect(left + (width - side) / 2, row.Y + (row.Height - side) / 2, side, side);
    }

    /// <summary>
    /// Puce carrée à broches, lisible dès 10 px.
    /// </summary>
    private static void DrawCpuIcon(DrawingContext context, Rect bounds, Brush color)
    {
        double unit = bounds.Width / 10;
        var pen = new Pen(color, Math.Max(1, unit));
        var body = new Rect(bounds.X + unit * 2.5, bounds.Y + unit * 2.5, unit * 5, unit * 5);
        context.DrawRectangle(null, pen, body);
        context.DrawRectangle(color, null, new Rect(body.X + unit * 1.5, body.Y + unit * 1.5, unit * 2, unit * 2));
        foreach (double offset in new[] { 3.5, 6.5 })
        {
            double along = bounds.X + unit * offset;
            double across = bounds.Y + unit * offset;
            context.DrawLine(pen, new Point(along, bounds.Y + unit * 0.5), new Point(along, body.Top));
            context.DrawLine(pen, new Point(along, body.Bottom), new Point(along, bounds.Bottom - unit * 0.5));
            context.DrawLine(pen, new Point(bounds.X + unit * 0.5, across), new Point(body.Left, across));
            context.DrawLine(pen, new Point(body.Right, across), new Point(bounds.Right - unit * 0.5, across));
        }
    }

    /// <summary>
    /// Barrette de mémoire vue de face, avec ses puces et ses contacts.
    /// </summary>
    private static void DrawMemoryIcon(DrawingContext context, Rect bounds, Brush color)
    {
        double unit = bounds.Width / 10;
        var pen = new Pen(color, Math.Max(1, unit));
        var stick = new Rect(bounds.X + unit * 0.5, bounds.Y + unit * 2.5, unit * 9, unit * 4);
        context.DrawRectangle(null, pen, stick);
        foreach (double offset in new[] { 1.5, 4, 6.5 })
            context.DrawRectangle(color, null, new Rect(bounds.X + unit * offset, stick.Y + unit, unit * 2, unit * 2));
        foreach (double offset in new[] { 2, 4, 6, 8 })
            context.DrawLine(pen, new Point(bounds.X + unit * offset, stick.Bottom),
                new Point(bounds.X + unit * offset, stick.Bottom + unit * 2));
    }

    private static void DrawHeroStack(DrawingContext context, Rect bounds, Meter[] quotas, Meter[] pc)
    {
        var hero = Hero(quotas, pc);
        var card = Part(bounds, 0, 0, 1, quotas.Length > 1 ? 0.51 : 0.72);
        DrawCard(context, card, hero.Color);
        DrawIdentity(context, hero, Part(card, 0.09, 0.05, 0.82, 0.26));
        if (hero.ProviderId.Length > 0)
            DrawText(context, hero.Label, Part(card, 0.08, 0.32, 0.84, 0.1), Font(bounds, 0.16), hero.IsStale ? Stale : Muted);
        DrawText(context, Percent(hero.Percent), Part(card, 0.06, 0.44, 0.88, 0.23), Font(bounds, 0.42), hero.Color);
        DrawBar(context, Part(card, 0.12, 0.72, 0.76, 0.035), hero);
        if (hero.ResetsAt != null)
        {
            // Le jour reste explicite : le même relevé ne change pas de pixels au passage de minuit.
            string reset = hero.ResetsAt.Value.ToString(Loc.T("Usage_DayTimeFormat"), Loc.Formatting);
            DrawText(context, reset, Part(card, 0.08, 0.82, 0.84, 0.11), Font(bounds, 0.14), hero.IsStale ? Stale : Muted);
        }

        var following = quotas.Skip(1).Take(3).ToArray();
        if (following.Length > 0)
        {
            var compact = Part(bounds, 0, 0.535, 1, 0.235);
            DrawCard(context, compact, Muted);
            for (int index = 0; index < following.Length; index++)
            {
                var row = Part(compact, 0.08, (double)index / following.Length, 0.84, 1d / following.Length);
                var meter = following[index];
                DrawText(context, meter.ProviderName, Part(row, 0, 0, 1, 0.4),
                    Font(bounds, 0.14), meter.IsStale ? Stale : Foreground);
                DrawText(context, ShortLabel(meter), Part(row, 0, 0.4, 0.5, 0.4),
                    Font(bounds, 0.14), meter.IsStale ? Stale : Muted);
                DrawText(context, Percent(meter.Percent), Part(row, 0.5, 0.4, 0.5, 0.4), Font(bounds, 0.16), meter.Color);
                DrawBar(context, Part(row, 0, 0.88, 1, 0.08), meter);
            }
        }
        for (int index = 0; index < pc.Length; index++)
        {
            var tile = Part(bounds, index * 0.52, 0.8, 0.48, 0.2);
            DrawCard(context, tile, pc[index].Color);
            DrawText(context, pc[index].Label, Part(tile, 0.04, 0.12, 0.92, 0.25), Font(bounds, 0.14), Muted);
            DrawText(context, Percent(pc[index].Percent), Part(tile, 0.04, 0.43, 0.92, 0.35), Font(bounds, 0.25), pc[index].Color);
        }
    }

    private static void DrawRings(DrawingContext context, Rect bounds, Meter[] quotas, Meter[] pc)
    {
        var providers = quotas.GroupBy(quota => quota.ProviderId, StringComparer.OrdinalIgnoreCase)
            .Take(2).Select(group => group.Take(2).ToArray()).ToArray();
        int count = providers.Length + 1;
        for (int index = 0; index < count; index++)
        {
            bool isPc = index == providers.Length;
            var meters = isPc ? pc : providers[index];
            var slot = Part(bounds, 0, (double)index / count, 1, 1d / count);
            bool onlyPc = providers.Length == 0;
            double radius = Math.Min(slot.Width * (onlyPc ? 0.47 : 0.44), slot.Height * (onlyPc ? 0.38 : 0.3));
            var center = new Point(slot.X + slot.Width / 2, slot.Y + slot.Height * (onlyPc ? 0.5 : 0.46));
            double thickness = radius * (onlyPc ? 0.1 : 0.12);
            for (int ring = 0; ring < meters.Length; ring++)
                DrawRing(context, center, radius - ring * thickness * 1.9, thickness, meters[ring]);
            double middleHeight = radius * (onlyPc ? 1.1 : 0.9);
            var middle = new Rect(center.X - radius * 0.57, center.Y - middleHeight / 2, radius * 1.14, middleHeight);
            if (isPc)
            {
                double valueSize = onlyPc ? radius * 0.55 : Font(bounds, 0.19);
                DrawText(context, Percent(pc[0].Percent), Part(middle, 0, 0, 1, 0.5), valueSize, Cpu);
                DrawText(context, Percent(pc[1].Percent), Part(middle, 0, 0.5, 1, 0.5), valueSize, Memory);
                var label = onlyPc
                    ? new Rect(slot.X, center.Y + radius * 1.15, slot.Width,
                        Math.Min(Font(bounds, 0.14) * 1.6, slot.Bottom - center.Y - radius * 1.15))
                    : Part(slot, 0.03, 0.81, 0.94, 0.16);
                DrawText(context, pc[0].Label + " / " + pc[1].Label, label, Font(bounds, 0.14), Muted);
            }
            else
            {
                DrawText(context, Percent(meters[0].Percent), middle, Font(bounds, 0.24), meters[0].Color);
                DrawIdentity(context, meters[0], Part(slot, 0.08, 0.015, 0.84, 0.16), horizontal: true);
                DrawText(context, meters[0].ProviderName, Part(slot, 0.02, 0.76, 0.96, 0.12),
                    Font(bounds, 0.16), meters[0].IsStale ? Stale : Foreground);
                DrawText(context, string.Join(" / ", meters.Select(meter => ShortLabel(meter))),
                    Part(slot, 0.02, 0.88, 0.96, 0.11), Font(bounds, 0.14), Muted);
            }
        }
    }

    private static void DrawRing(DrawingContext context, Point center, double radius, double thickness, Meter meter, Brush? track = null)
    {
        context.DrawEllipse(null, new Pen(track ?? Track, thickness), center, radius, radius);
        double fraction = Fraction(meter.Percent);
        if (fraction <= 0)
            return;
        var pen = new Pen(meter.Color, thickness) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round };
        if (fraction >= 1)
        {
            context.DrawEllipse(null, pen, center, radius, radius);
            return;
        }
        double angle = fraction * Math.PI * 2;
        var geometry = new StreamGeometry();
        using (var path = geometry.Open())
        {
            path.BeginFigure(new Point(center.X, center.Y - radius), isFilled: false, isClosed: false);
            path.ArcTo(new Point(center.X + Math.Sin(angle) * radius, center.Y - Math.Cos(angle) * radius),
                new Size(radius, radius), rotationAngle: 0, isLargeArc: fraction > 0.5,
                sweepDirection: SweepDirection.Clockwise, isStroked: true, isSmoothJoin: true);
        }
        geometry.Freeze();
        context.DrawGeometry(null, pen, geometry);
    }

    private static void DrawReservoir(DrawingContext context, Rect bounds, Meter[] quotas, Meter[] pc)
    {
        var hero = Hero(quotas, pc);
        var tank = Part(bounds, 0, 0, 1, 0.68);
        var clip = new RectangleGeometry(tank, bounds.Width * 0.15, bounds.Width * 0.15);
        context.DrawGeometry(Track, null, clip);
        context.PushClip(clip);
        double fill = tank.Height * Fraction(hero.Percent);
        double level = tank.Bottom - fill;
        double wave = Math.Min(tank.Height * 0.012, Math.Min(fill, tank.Height - fill) / 3);
        var surface = new StreamGeometry();
        using (var path = surface.Open())
        {
            path.BeginFigure(new Point(tank.Left, level), isFilled: false, isClosed: false);
            path.BezierTo(new Point(tank.Left + tank.Width / 3, level - wave),
                new Point(tank.Left + tank.Width * 2 / 3, level + wave), new Point(tank.Right, level), true, true);
        }
        surface.Freeze();
        if (fill > 0)
        {
            var liquid = new StreamGeometry();
            using (var path = liquid.Open())
            {
                path.BeginFigure(new Point(tank.Left, level), isFilled: true, isClosed: true);
                path.BezierTo(new Point(tank.Left + tank.Width / 3, level - wave),
                    new Point(tank.Left + tank.Width * 2 / 3, level + wave), new Point(tank.Right, level), true, true);
                path.LineTo(new Point(tank.Right, tank.Bottom), true, false);
                path.LineTo(new Point(tank.Left, tank.Bottom), true, false);
            }
            liquid.Freeze();
            context.PushOpacity(0.3);
            context.DrawGeometry(hero.Color, null, liquid);
            context.Pop();
        }

        double gap = Math.Max(2, bounds.Width * 0.04);
        double surfaceGap = wave + gap;
        double headerHeight = tank.Height * 0.26;
        double headerTop = tank.Top + gap;
        if (headerTop + headerHeight >= level - surfaceGap)
            headerTop = level + surfaceGap;
        var header = new Rect(tank.X + tank.Width * 0.07, headerTop, tank.Width * 0.86, headerHeight);
        DrawIdentity(context, hero, Part(header, 0, 0, 1, 0.7));
        if (hero.ProviderId.Length > 0)
            DrawText(context, hero.Label, Part(header, 0, 0.76, 1, 0.24), Font(bounds, 0.16), hero.IsStale ? Stale : Foreground);

        double valueHeight = tank.Height * 0.14;
        double liquidTop = Math.Max(level + surfaceGap, header.Bottom + gap);
        double valueTop = tank.Bottom - gap - liquidTop >= valueHeight
            ? liquidTop + (tank.Bottom - gap - liquidTop - valueHeight) / 2
            : level - surfaceGap - valueHeight;
        DrawText(context, Percent(hero.Percent), new Rect(tank.X + tank.Width * 0.06, valueTop, tank.Width * 0.88, valueHeight),
            Font(bounds, 0.43), hero.Color);
        string? heroRemaining = SessionRemaining(hero);
        if (heroRemaining != null)
        {
            double remainingHeight = tank.Height * 0.07;
            double remainingTop = valueTop + valueHeight + remainingHeight <= tank.Bottom - gap
                ? valueTop + valueHeight
                : valueTop - remainingHeight;
            DrawText(context, Loc.F("StreamDock_ResetIn", heroRemaining),
                new Rect(tank.X + tank.Width * 0.06, remainingTop, tank.Width * 0.88, remainingHeight),
                Font(bounds, 0.16), hero.IsStale ? Stale : Foreground);
        }
        context.DrawGeometry(null, new Pen(hero.Color, Math.Max(1, bounds.Width * 0.02)), surface);
        context.Pop();

        Meter?[] badges = [quotas.ElementAtOrDefault(1), quotas.ElementAtOrDefault(2), pc[0], pc[1]];
        for (int index = 0; index < badges.Length; index++)
        {
            var cell = Part(bounds, index % 2 * 0.52, 0.7 + index / 2 * 0.15, 0.48, 0.15);
            double radius = Math.Min(cell.Width * 0.45, cell.Height * 0.31);
            double thickness = Math.Max(1, radius * 0.12);
            var center = new Point(cell.X + cell.Width / 2, cell.Y + radius + cell.Height * 0.04);
            var meter = badges[index];
            if (meter == null)
            {
                context.DrawEllipse(null, new Pen(Muted, thickness), center, radius, radius);
                continue;
            }
            DrawRing(context, center, radius, thickness, meter, track: Muted);
            DrawText(context, Percent(meter.Percent), new Rect(center.X - radius * 0.9, center.Y - radius * 0.55,
                radius * 1.8, radius * 1.1), Font(bounds, 0.17), meter.Color);
            string? remaining = SessionRemaining(meter);
            DrawText(context, remaining == null ? ShortLabel(meter) : ShortLabel(meter) + "·" + remaining,
                Part(cell, 0.02, 0.7, 0.96, 0.27), Font(bounds, 0.14), meter.IsStale ? Stale : Muted);
        }
    }

    private static void DrawIdentity(DrawingContext context, Meter meter, Rect bounds, bool horizontal = false)
    {
        if (meter.ProviderId.Length == 0)
        {
            DrawText(context, meter.ProviderName, bounds, Math.Max(9, bounds.Width * 0.23), meter.Color);
            return;
        }
        double side = Math.Min(bounds.Width * 0.3, bounds.Height * (horizontal ? 0.9 : 0.48));
        var logo = new Rect(bounds.X + (bounds.Width - side) / 2, bounds.Y, side, side);
        DrawProvider(context, meter, logo);
        if (horizontal == false)
            DrawText(context, meter.ProviderName, Part(bounds, 0, 0.6, 1, 0.4),
                Math.Max(9, bounds.Width * 0.18), meter.IsStale ? Stale : Foreground);
    }

    private static void DrawProvider(DrawingContext context, Meter meter, Rect bounds)
    {
        Brush color = meter.IsStale ? Stale : IdentityColor(meter.ProviderId);
        var geometry = Logo(meter.ProviderId);
        if (geometry != null)
        {
            var source = geometry.Bounds;
            double scale = Math.Min(bounds.Width / source.Width, bounds.Height / source.Height);
            context.PushTransform(new TranslateTransform(bounds.X + (bounds.Width - source.Width * scale) / 2 - source.X * scale,
                bounds.Y + (bounds.Height - source.Height * scale) / 2 - source.Y * scale));
            context.PushTransform(new ScaleTransform(scale, scale));
            context.DrawGeometry(color, null, geometry);
            context.Pop();
            context.Pop();
            return;
        }
        context.DrawRoundedRectangle(Track, null, bounds, bounds.Width / 5, bounds.Height / 5);
        string initial = StringInfo.GetNextTextElement(meter.ProviderId).ToUpper(Loc.Formatting);
        DrawText(context, initial, bounds, Math.Max(9, bounds.Height * 0.7), color);
    }

    private static Geometry? Logo(string providerId) => providerId.ToLowerInvariant() switch
    {
        "claude" => ProviderLogos.Claude,
        "codex" => ProviderLogos.Codex,
        "antigravity" => ProviderLogos.Antigravity,
        _ => null,
    };

    private static void DrawCard(DrawingContext context, Rect bounds, Brush tint)
    {
        context.DrawRoundedRectangle(Track, null, bounds, bounds.Width * 0.13, bounds.Width * 0.13);
        context.PushOpacity(0.09);
        context.DrawRoundedRectangle(tint, null, bounds, bounds.Width * 0.13, bounds.Width * 0.13);
        context.Pop();
    }

    private static void DrawBar(DrawingContext context, Rect bounds, Meter meter)
    {
        var clip = new RectangleGeometry(bounds, bounds.Height / 2, bounds.Height / 2);
        context.DrawGeometry(Track, null, clip);
        context.PushClip(clip);
        context.DrawRectangle(meter.Color, null, new Rect(bounds.X, bounds.Y, bounds.Width * Fraction(meter.Percent), bounds.Height));
        context.Pop();
    }

    private static void DrawText(DrawingContext context, string text, Rect bounds, double fontSize, Brush fill)
    {
        if (bounds.Width <= 0 || bounds.Height <= 0)
            return;
        double size = Math.Min(fontSize, bounds.Height / Measure(text, 1).Height);
        var fitted = Fit(text, bounds.Width, size, minFontSize: Math.Min(9, size));
        if (fitted.Text.Length == 0)
            return;
        var formatted = Measure(fitted.Text, fitted.FontSize, fill);
        if (formatted.Height > bounds.Height)
        {
            size = fitted.FontSize * bounds.Height / formatted.Height * 0.999;
            fitted = Fit(text, bounds.Width, size, minFontSize: Math.Min(9, size));
            formatted = Measure(fitted.Text, fitted.FontSize, fill);
        }
        var origin = new Point(bounds.X + (bounds.Width - formatted.WidthIncludingTrailingWhitespace) / 2,
            bounds.Y + (bounds.Height - formatted.Height) / 2);
        context.DrawText(formatted, origin);
        TextDrawn?.Invoke(new DrawnText(text, fitted.Text, bounds,
            new Size(formatted.WidthIncludingTrailingWhitespace, formatted.Height), fitted.FontSize));
    }

    private static FormattedText Measure(string text, double fontSize, Brush? fill = null) =>
        new(text, Loc.Formatting, FlowDirection.LeftToRight, Typeface, fontSize, fill ?? Foreground, pixelsPerDip: 1);

    private static string ShortLabel(Meter meter) => meter.WindowKind switch
    {
        UsageWindowKind.Session => Loc.T("StreamDock_SessionShort"),
        UsageWindowKind.Week => Loc.T("StreamDock_WeekShort"),
        _ => meter.Label,
    };

    /// <summary>
    /// Temps restant d'une fenêtre 5 h : en heures entières, puis en minutes sous l'heure.
    /// </summary>
    private static string? SessionRemaining(Meter meter)
    {
        if (meter.WindowKind != UsageWindowKind.Session || meter.ResetIn is not { } remaining)
            return null;
        int minutes = (int)Math.Ceiling(remaining.TotalMinutes);
        return minutes < 60
            ? Loc.F("StreamDock_RemainingMinutes", minutes)
            : Loc.F("StreamDock_RemainingHours", (int)remaining.TotalHours);
    }

    private static string Percent(int? value, bool withSymbol = true) => value == null ? "—" :
        Math.Clamp(value.Value, 0, 100).ToString(Loc.Formatting) + (withSymbol ? "%" : "");

    private static double Fraction(int? value) => value == null ? 0 : Math.Clamp(value.Value, 0, 100) / 100d;

    private static double Font(Rect bounds, double widthRatio) => Math.Max(9, bounds.Width * widthRatio);

    private static Rect Part(Rect bounds, double x, double y, double width, double height) =>
        new(bounds.X + bounds.Width * x, bounds.Y + bounds.Height * y, bounds.Width * width, bounds.Height * height);

    private static Brush FrozenBrush(byte red, byte green, byte blue)
    {
        var brush = new SolidColorBrush(Color.FromRgb(red, green, blue));
        brush.Freeze();
        return brush;
    }

    private static Brush CreateAlertBrush()
    {
        var color = ColorConverter.ConvertFromString(UsageFormat.Critical);
        if (color is not Color value)
            throw new InvalidOperationException("Invalid usage alert color.");
        return FrozenBrush(value.R, value.G, value.B);
    }
}
