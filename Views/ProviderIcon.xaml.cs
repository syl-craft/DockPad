using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace DockPad.Views;

/// <summary>Logo vectoriel du fournisseur, ou pastille pour les autres fournisseurs.</summary>
public partial class ProviderIcon : UserControl
{
    public static readonly DependencyProperty ProviderIdProperty = DependencyProperty.Register(
        nameof(ProviderId), typeof(string), typeof(ProviderIcon), new PropertyMetadata(""));
    public static readonly DependencyProperty GlyphProperty = DependencyProperty.Register(
        nameof(Glyph), typeof(string), typeof(ProviderIcon), new PropertyMetadata(""));
    public static readonly DependencyProperty AccentProperty = DependencyProperty.Register(
        nameof(Accent), typeof(Brush), typeof(ProviderIcon), new PropertyMetadata(Brushes.Gray));

    public string ProviderId
    {
        get => (string)GetValue(ProviderIdProperty);
        set => SetValue(ProviderIdProperty, value);
    }

    public string Glyph
    {
        get => (string)GetValue(GlyphProperty);
        set => SetValue(GlyphProperty, value);
    }

    public Brush Accent
    {
        get => (Brush)GetValue(AccentProperty);
        set => SetValue(AccentProperty, value);
    }

    public ProviderIcon() => InitializeComponent();
}
