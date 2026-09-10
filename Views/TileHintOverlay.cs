using System.Windows;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using DockPad.Services;

namespace DockPad.Views;

/// <summary>Affiche les indications clavier dans une grille superposée aux tuiles.</summary>
public sealed class TileHintOverlay(UniformGrid panel)
{
    private readonly UniformGrid _panel = panel;
    private readonly List<UIElement> _hintElements = [];
    private static readonly Brush HintVeil = Frozen(0x55, 0x60);
    private static readonly Brush HintBadge = Frozen(0xBB, 0x55);

    private static Brush Frozen(byte alpha, byte gray)
    {
        var brush = new SolidColorBrush(Color.FromArgb(alpha, gray, gray, gray));
        brush.Freeze();
        return brush;
    }

    public void Show(bool isCtrl)
    {
        Hide();
        _panel.Visibility = Visibility.Visible;

        for (int row = 0; row < ShortcutActionService.GridRows; row++)
        {
            for (int col = 0; col < ShortcutActionService.GridCols; col++)
            {
                if (isCtrl ? col >= 3 : col < 3) continue; // côté inactif

                // Rows 0-2 : chiffres 1-9 ; row 3 : 0, ↑, ↓
                string label = row < 3
                    ? (row * 3 + col % 3 + 1).ToString()
                    : (col % 3) switch { 0 => "0", 1 => "↑", _ => "↓" };

                AddHintOverlayElement(row, col, new Border
                {
                    Margin = new Thickness(5),
                    Background = HintVeil,
                    IsHitTestVisible = false,
                    CornerRadius = new CornerRadius(6),
                    SnapsToDevicePixels = true, UseLayoutRounding = true,
                });

                AddHintOverlayElement(row, col, new Border
                {
                    Width = 20, Height = 20,
                    HorizontalAlignment = HorizontalAlignment.Left,
                    VerticalAlignment = VerticalAlignment.Top,
                    Margin = new Thickness(12, 12, 0, 0),
                    Background = HintBadge,
                    CornerRadius = new CornerRadius(4),
                    IsHitTestVisible = false,
                    SnapsToDevicePixels = true, UseLayoutRounding = true,
                    Child = new TextBlock
                    {
                        Text = label,
                        FontSize = 11, FontWeight = FontWeights.SemiBold,
                        Foreground = Brushes.White,
                        HorizontalAlignment = HorizontalAlignment.Center,
                        VerticalAlignment = VerticalAlignment.Center,
                    }
                });
            }
        }
    }

    /// <summary>Ajoute un élément à la cellule située à l'index row × colonnes + col.</summary>
    private void AddHintOverlayElement(int row, int col, UIElement element)
    {
        EnsureHintCells();

        if (_panel.Children[row * ShortcutActionService.GridCols + col] is Grid cell)
            cell.Children.Add(element);

        _hintElements.Add(element);
    }

    /// <summary>Un conteneur par case, cree une seule fois.</summary>
    private void EnsureHintCells()
    {
        if (_panel.Children.Count > 0) return;

        for (int i = 0; i < ShortcutActionService.GridRows * ShortcutActionService.GridCols; i++)
            _panel.Children.Add(new Grid());
    }

    public void Hide()
    {
        foreach (var element in _hintElements)
            if (VisualTreeHelper.GetParent(element) is Grid cell)
                cell.Children.Remove(element);

        _hintElements.Clear();
        _panel.Visibility = Visibility.Collapsed;
    }

}
