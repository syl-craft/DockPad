using System.Windows;
using System.Windows.Media;

namespace DockPad.Views;

/// <summary>
/// Jauge pleine à une fraction : une piste, et un remplissage qui part du bas (vertical) ou de la gauche.
/// </summary>
/// <remarks>
/// Dessinée directement : un ProgressBar vertical ne remplit rien dans un gabarit sans rotation, et la barre n'a pas
/// besoin de ses états.
/// </remarks>
public sealed class FractionBar : FrameworkElement
{
    public static readonly DependencyProperty FractionProperty = Register(nameof(Fraction), 0d);
    public static readonly DependencyProperty FillProperty = Register(nameof(Fill), Brushes.Gray);
    public static readonly DependencyProperty TrackProperty = Register<Brush?>(nameof(Track), null);
    public static readonly DependencyProperty RadiusProperty = Register(nameof(Radius), 0d);
    public static readonly DependencyProperty FillOpacityProperty = Register(nameof(FillOpacity), 1d);
    public static readonly DependencyProperty IsVerticalProperty = Register(nameof(IsVertical), false);

    private static DependencyProperty Register<T>(string name, T defaultValue) =>
        DependencyProperty.Register(name, typeof(T), typeof(FractionBar),
            new FrameworkPropertyMetadata(defaultValue, FrameworkPropertyMetadataOptions.AffectsRender));

    /// <summary>De 0 (vide) à 1 (plein).</summary>
    public double Fraction
    {
        get => (double)GetValue(FractionProperty);
        set => SetValue(FractionProperty, value);
    }

    public Brush Fill
    {
        get => (Brush)GetValue(FillProperty);
        set => SetValue(FillProperty, value);
    }

    public Brush? Track
    {
        get => (Brush?)GetValue(TrackProperty);
        set => SetValue(TrackProperty, value);
    }

    public double Radius
    {
        get => (double)GetValue(RadiusProperty);
        set => SetValue(RadiusProperty, value);
    }

    public double FillOpacity
    {
        get => (double)GetValue(FillOpacityProperty);
        set => SetValue(FillOpacityProperty, value);
    }

    public bool IsVertical
    {
        get => (bool)GetValue(IsVerticalProperty);
        set => SetValue(IsVerticalProperty, value);
    }

    protected override void OnRender(DrawingContext drawingContext)
    {
        base.OnRender(drawingContext);
        if (ActualWidth <= 0 || ActualHeight <= 0) return;

        var bounds = new Rect(0, 0, ActualWidth, ActualHeight);
        var radius = Math.Min(Radius, Math.Min(ActualWidth, ActualHeight) / 2);
        drawingContext.DrawRoundedRectangle(Track, null, bounds, radius, radius);

        var fraction = Math.Clamp(double.IsNaN(Fraction) ? 0 : Fraction, 0, 1);
        if (fraction <= 0) return;

        var filled = IsVertical
            ? new Rect(0, ActualHeight * (1 - fraction), ActualWidth, ActualHeight * fraction)
            : new Rect(0, 0, ActualWidth * fraction, ActualHeight);

        // Le remplissage suit les coins de la piste : un aplat rectangulaire en dépasserait.
        var clip = new RectangleGeometry(bounds, radius, radius);
        clip.Freeze();
        drawingContext.PushClip(clip);
        drawingContext.PushOpacity(FillOpacity);
        drawingContext.DrawRectangle(Fill, null, filled);
        drawingContext.Pop();
        drawingContext.Pop();
    }
}
