using System.Windows;
using System.Windows.Media;

namespace DockPad.Views;

/// <summary>
/// Courbe d'une série de mesures, la plus récente à droite. Une valeur nulle laisse un trou, pas un zéro.
/// </summary>
public sealed class LoadCurve : FrameworkElement
{
    /// <summary>Nombre de mesures que l'axe représente : la courbe part de la droite et la gauche reste vide.</summary>
    private const int Slots = 12;

    public static readonly DependencyProperty SeriesProperty = DependencyProperty.Register(
        nameof(Series), typeof(IReadOnlyList<int?>), typeof(LoadCurve),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty StrokeProperty = DependencyProperty.Register(
        nameof(Stroke), typeof(Brush), typeof(LoadCurve),
        new FrameworkPropertyMetadata(Brushes.Gray, FrameworkPropertyMetadataOptions.AffectsRender));

    public IReadOnlyList<int?>? Series
    {
        get => (IReadOnlyList<int?>?)GetValue(SeriesProperty);
        set => SetValue(SeriesProperty, value);
    }

    public Brush Stroke
    {
        get => (Brush)GetValue(StrokeProperty);
        set => SetValue(StrokeProperty, value);
    }

    protected override void OnRender(DrawingContext drawingContext)
    {
        base.OnRender(drawingContext);
        if (Series is not { Count: > 0 } series || ActualWidth <= 0 || ActualHeight <= 0) return;

        var pen = new Pen(Stroke, 1.4) { LineJoin = PenLineJoin.Round, StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round };
        pen.Freeze();

        var firstSlot = Slots - series.Count;
        var figure = new List<Point>();
        for (var index = 0; index < series.Count; index++)
        {
            if (series[index] is not { } percent)
            {
                DrawFigure(drawingContext, pen, figure);
                figure.Clear();
                continue;
            }

            var x = ActualWidth * (firstSlot + index) / (Slots - 1);
            var y = ActualHeight - 1 - Math.Clamp(percent, 0, 100) / 100d * (ActualHeight - 2);
            figure.Add(new Point(x, y));
        }
        DrawFigure(drawingContext, pen, figure);
    }

    private static void DrawFigure(DrawingContext drawingContext, Pen pen, List<Point> points)
    {
        if (points.Count == 1)
        {
            drawingContext.DrawEllipse(pen.Brush, null, points[0], 1, 1);
            return;
        }
        if (points.Count < 2) return;

        var figure = new PathFigure(points[0], [new PolyLineSegment(points.Skip(1), true)], closed: false);
        drawingContext.DrawGeometry(null, pen, new PathGeometry([figure]));
    }
}
