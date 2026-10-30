using System.Windows;
using System.Windows.Media;
using System.Windows.Shapes;

namespace DockPad.Views;

/// <summary>
/// Arc de cercle qui part du haut et tourne dans le sens horaire sur la fraction demandée. Le cercle occupe
/// <see cref="FrameworkElement.Width"/> × <see cref="FrameworkElement.Height"/>, trait compris.
/// </summary>
public sealed class RingArc : Shape
{
    public static readonly DependencyProperty FractionProperty = DependencyProperty.Register(
        nameof(Fraction), typeof(double), typeof(RingArc),
        new FrameworkPropertyMetadata(0d, FrameworkPropertyMetadataOptions.AffectsRender));

    public RingArc()
    {
        StrokeStartLineCap = PenLineCap.Round;
        StrokeEndLineCap = PenLineCap.Round;
    }

    /// <summary>De 0 (rien) à 1 (cercle complet).</summary>
    public double Fraction
    {
        get => (double)GetValue(FractionProperty);
        set => SetValue(FractionProperty, value);
    }

    protected override Geometry DefiningGeometry
    {
        get
        {
            var diameter = Math.Min(Width, Height);
            var fraction = Math.Clamp(double.IsNaN(Fraction) ? 0 : Fraction, 0, 1);
            if (double.IsNaN(diameter) || diameter <= StrokeThickness || fraction <= 0) return Geometry.Empty;

            var radius = (diameter - StrokeThickness) / 2;
            var center = new Point(diameter / 2, diameter / 2);
            if (fraction >= 0.999) return new EllipseGeometry(center, radius, radius);

            var angle = 2 * Math.PI * fraction - Math.PI / 2;
            var end = new Point(center.X + radius * Math.Cos(angle), center.Y + radius * Math.Sin(angle));
            var figure = new PathFigure(new Point(center.X, center.Y - radius), 
                [new ArcSegment(end, new Size(radius, radius), 0, fraction > 0.5, SweepDirection.Clockwise, true)],
                closed: false);
            return new PathGeometry([figure]);
        }
    }
}
