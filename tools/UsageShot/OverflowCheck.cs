using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using DockPad.Views;

namespace UsageShot;

/// <summary>
/// Parcourt l'arbre visuel de la barre mise en page : tout texte ou forme hors de la barre ou de son parent, et tout
/// texte tronqué sans infobulle au texte complet, est un écart.
/// </summary>
/// <remarks>
/// La visibilité est relue élément par élément, <c>IsVisible</c> valant faux sur une fenêtre jamais affichée.
/// Un texte qui porte un « … », où qu'il soit, compte comme raccourci : chaque morceau raccourci doit retrouver son
/// début dans l'infobulle, qui doit être plus longue que le texte.
/// </remarks>
internal static class OverflowCheck
{
    /// <summary>
    /// Largeur de la barre.
    /// </summary>
    public const double BarWidth = 90;

    private const double Tolerance = 0.5;
    private const string Ellipsis = "…";

    /// <summary>
    /// Sépare les morceaux d'un texte composé, comme la légende « Clau… / Gemi… ».
    /// </summary>
    private const string PieceSeparator = " / ";

    /// <summary>
    /// Le point d'alerte déborde de 3 du coin de son logo à dessein ; il reste contrôlé contre les bornes de la barre.
    /// </summary>
    private const string OverhangingDot = "AlertDot";

    /// <summary>
    /// Un écart : où il est, quel élément, et ce qui ne va pas.
    /// </summary>
    internal sealed record Violation(string Context, string Element, string Problem)
    {
        public override string ToString() => $"[{Context}] {Element} : {Problem}";
    }

    /// <summary>
    /// Résultat du contrôle d'une barre : les écarts, et le nombre d'éléments vérifiés.
    /// </summary>
    internal sealed record Report(IReadOnlyList<Violation> Violations, int TextCount, int ShapeCount);

    public static Report Inspect(UsageBar bar, string context)
    {
        var violations = new List<Violation>();
        var counts = new int[2];

        if (Math.Abs(bar.RenderSize.Width - BarWidth) > Tolerance)
            violations.Add(new Violation(context, "UsageBar", $"largeur {bar.RenderSize.Width:0.##} au lieu de {BarWidth}"));

        Visit(bar, bar, context, violations, counts);
        return new Report(violations, counts[0], counts[1]);
    }

    private static void Visit(DependencyObject node, UsageBar bar, string context, List<Violation> violations, int[] counts)
    {
        if (node is UIElement element && IsShown(element, bar))
        {
            switch (element)
            {
                case TextBlock { Text.Length: > 0 } text:
                    counts[0]++;
                    CheckText(text, bar, context, violations);
                    break;
                case Shape or FractionBar or LoadCurve:
                    counts[1]++;
                    CheckShape((FrameworkElement)element, bar, context, violations);
                    break;
            }
        }
        else if (node is UIElement)
        {
            return;   // un élément masqué ne montre rien, ni lui ni ses descendants
        }

        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(node); index++)
            Visit(VisualTreeHelper.GetChild(node, index), bar, context, violations, counts);
    }

    /// <summary>
    /// Visible, lui et chacun de ses ancêtres jusqu'à la barre, et de taille non nulle.
    /// </summary>
    private static bool IsShown(UIElement element, UsageBar bar)
    {
        if (element.RenderSize.Width <= 0 || element.RenderSize.Height <= 0) return false;

        for (DependencyObject? current = element; current != null; current = VisualTreeHelper.GetParent(current))
        {
            if (current is UIElement { Visibility: not Visibility.Visible }) return false;
            if (current is UIElement { Opacity: <= 0 }) return false;
            if (ReferenceEquals(current, bar)) break;
        }
        return true;
    }

    // --- Texte

    private static void CheckText(TextBlock text, UsageBar bar, string context, List<Violation> violations)
    {
        var name = Describe(text, text.Text);
        CheckBounds(text, new Rect(text.RenderSize), bar, context, name, violations);

        var shortened = text.Text.Contains(Ellipsis, StringComparison.Ordinal);
        var clipped = IsClipped(text);
        if (clipped && text.TextTrimming == TextTrimming.None)
        {
            violations.Add(new Violation(context, name,
                $"le texte dépasse son cadre ({text.ActualWidth:0.#} × {text.ActualHeight:0.#}) sans troncature"));
            return;
        }

        if (clipped == false && shortened == false) return;

        var tooltip = FindTooltip(text, bar);
        if (tooltip == null)
        {
            violations.Add(new Violation(context, name, "texte tronqué sans infobulle"));
            return;
        }

        // Un texte raccourci rend son début ; un texte coupé par la mise en page se retrouve en entier.
        var needles = shortened ? ShortenedPrefixes(text.Text) : [text.Text];
        var complete = needles.All(needle => tooltip.Contains(needle, StringComparison.Ordinal))
                       && (shortened == false || tooltip.Length > text.Text.Length);
        if (complete == false)
            violations.Add(new Violation(context, name, $"l'infobulle ne porte pas le texte complet (« {Flatten(tooltip)} »)"));
    }

    /// <summary>
    /// Pour chaque « … » du texte, ce qui le précède dans son morceau : « Clau… / Gemi… » donne « Clau » et « Gemi ».
    /// </summary>
    private static IEnumerable<string> ShortenedPrefixes(string text)
    {
        foreach (var piece in text.Split(PieceSeparator))
        {
            var segments = piece.Split(Ellipsis);
            // Le dernier segment suit le dernier « … » : il n'est pas raccourci.
            foreach (var segment in segments.Take(segments.Length - 1))
            {
                if (segment.Length > 0) yield return segment;
            }
        }
    }

    /// <summary>
    /// Le texte est coupé par la mise en page : plus large que son cadre, ou plus de lignes qu'il n'a de hauteur.
    /// </summary>
    private static bool IsClipped(TextBlock text)
    {
        var typeface = new Typeface(text.FontFamily, text.FontStyle, text.FontWeight, text.FontStretch);
        var pixelsPerDip = VisualTreeHelper.GetDpi(text).PixelsPerDip;
        var formatted = new FormattedText(text.Text, CultureInfo.CurrentUICulture, text.FlowDirection, typeface,
            text.FontSize, Brushes.Black, pixelsPerDip);

        var availableWidth = text.ActualWidth - text.Padding.Left - text.Padding.Right;
        if (text.TextWrapping == TextWrapping.NoWrap)
            return formatted.WidthIncludingTrailingWhitespace > availableWidth + Tolerance;

        formatted.MaxTextWidth = Math.Max(availableWidth, 1);
        return formatted.Height > text.ActualHeight - text.Padding.Top - text.Padding.Bottom + Tolerance;
    }

    /// <summary>
    /// L'infobulle de l'élément, ou celle du premier ancêtre qui en porte une, jusqu'à la barre comprise : WPF affiche
    /// celle de l'ancêtre quand l'élément survolé n'en a pas.
    /// </summary>
    private static string? FindTooltip(DependencyObject start, UsageBar bar)
    {
        for (var current = start; current != null; current = VisualTreeHelper.GetParent(current))
        {
            if (current is FrameworkElement { ToolTip: { } tip })
            {
                var content = tip is ToolTip toolTip ? toolTip.Content : tip;
                var text = content?.ToString() ?? "";
                if (text.Length > 0) return text;
            }
            if (ReferenceEquals(current, bar)) break;
        }
        return null;
    }

    // --- Formes

    private static void CheckShape(FrameworkElement shape, UsageBar bar, string context, List<Violation> violations)
    {
        var bounds = new Rect(shape.RenderSize);
        if (shape is Shape { RenderedGeometry: { } geometry } drawn && geometry.Bounds.IsEmpty == false)
        {
            var geometryBounds = geometry.Bounds;
            geometryBounds.Inflate(drawn.StrokeThickness / 2, drawn.StrokeThickness / 2);
            // La géométrie d'un tracé étiré tient dans sa boîte ; un tracé non étiré peut en sortir.
            if (drawn.Stretch == Stretch.None) bounds.Union(geometryBounds);
        }

        CheckBounds(shape, bounds, bar, context, Describe(shape, ""), violations);
    }

    // --- Bornes

    /// <summary>
    /// <paramref name="local"/> est dans les coordonnées de l'élément : il doit tenir dans la barre, et dans son
    /// parent direct sauf le point d'alerte.
    /// </summary>
    private static void CheckBounds(FrameworkElement element, Rect local, UsageBar bar, string context, string name,
        List<Violation> violations)
    {
        var inBar = element.TransformToAncestor(bar).TransformBounds(local);
        var barBounds = new Rect(bar.RenderSize);
        if (Contains(barBounds, inBar) == false)
            violations.Add(new Violation(context, name, $"hors de la barre : {Format(inBar)} pour {Format(barBounds)}"));

        if (element.Name == OverhangingDot) return;
        if (VisualParent(element) is not { } parent) return;

        var inParent = element.TransformToAncestor(parent).TransformBounds(local);
        var parentBounds = new Rect(parent.RenderSize);
        if (Contains(parentBounds, inParent) == false)
        {
            violations.Add(new Violation(context, name,
                $"hors de son parent {parent.GetType().Name} : {Format(inParent)} pour {Format(parentBounds)}"));
        }
    }

    private static UIElement? VisualParent(DependencyObject element)
    {
        for (var current = VisualTreeHelper.GetParent(element); current != null; current = VisualTreeHelper.GetParent(current))
        {
            if (current is UIElement { RenderSize.Width: > 0 } parent) return parent;
        }
        return null;
    }

    private static bool Contains(Rect outer, Rect inner) =>
        inner.Left >= outer.Left - Tolerance && inner.Top >= outer.Top - Tolerance
        && inner.Right <= outer.Right + Tolerance && inner.Bottom <= outer.Bottom + Tolerance;

    // --- Rapport

    private static string Describe(FrameworkElement element, string text)
    {
        var label = element.GetType().Name + (element.Name.Length > 0 ? $"#{element.Name}" : "");
        return text.Length > 0 ? $"{label} « {Flatten(text)} »" : label;
    }

    private static string Flatten(string text) => text.Replace("\r", "").Replace("\n", " / ");

    private static string Format(Rect rect) =>
        string.Create(CultureInfo.InvariantCulture, $"x {rect.Left:0.#}..{rect.Right:0.#}, y {rect.Top:0.#}..{rect.Bottom:0.#}");
}
