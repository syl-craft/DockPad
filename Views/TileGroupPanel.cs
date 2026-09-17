using System.Windows;
using System.Windows.Controls;
using DockPad.Models;

namespace DockPad.Views;

/// <summary>Répartit la surface disponible, séparateurs compris, sans agrandir la tuile.</summary>
public sealed class TileGroupPanel : Panel
{
    public static readonly DependencyProperty LayoutProperty = DependencyProperty.Register(
        nameof(Layout), typeof(TileLayout), typeof(TileGroupPanel),
        new FrameworkPropertyMetadata(TileLayout.Quad, FrameworkPropertyMetadataOptions.AffectsMeasure));

    public TileLayout Layout
    {
        get => (TileLayout)GetValue(LayoutProperty);
        set => SetValue(LayoutProperty, value);
    }

    public static Rect Bounds(TileLayout layout, int index, Size size)
    {
        if (layout == TileLayout.TwoPlusFour)
            return index < 2
                ? new Rect(index * size.Width / 2, 0, size.Width / 2, size.Height * 2 / 3)
                : new Rect((index - 2) * size.Width / 4, size.Height * 2 / 3, size.Width / 4, size.Height / 3);
        return new Rect(index % 2 * size.Width / 2, index / 2 * size.Height / 2, size.Width / 2, size.Height / 2);
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        var size = new Size(double.IsInfinity(availableSize.Width) ? 106 : availableSize.Width,
                            double.IsInfinity(availableSize.Height) ? 88 : availableSize.Height);
        for (int i = 0; i < InternalChildren.Count; i++)
            InternalChildren[i].Measure(Bounds(Layout, i, size).Size);
        return size;
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        for (int i = 0; i < InternalChildren.Count; i++)
            InternalChildren[i].Arrange(Bounds(Layout, i, finalSize));
        return finalSize;
    }
}
