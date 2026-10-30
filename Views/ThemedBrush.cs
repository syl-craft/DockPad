using System.Windows;
using System.Windows.Controls;

namespace DockPad.Views;

/// <summary>
/// Colore le texte d'un TextBlock avec une brosse du thème nommée par le modèle de vue. La référence est dynamique : un
/// changement de thème à chaud repeint le texte.
/// </summary>
public static class ThemedBrush
{
    public static readonly DependencyProperty KeyProperty = DependencyProperty.RegisterAttached(
        "Key", typeof(string), typeof(ThemedBrush), new PropertyMetadata("", OnKeyChanged));

    public static string GetKey(DependencyObject element) => (string)element.GetValue(KeyProperty);

    public static void SetKey(DependencyObject element, string value) => element.SetValue(KeyProperty, value);

    /// <summary>
    /// Une clé vide rend la main à la couleur que le style lie.
    /// </summary>
    private static void OnKeyChanged(DependencyObject element, DependencyPropertyChangedEventArgs e)
    {
        if (element is not TextBlock text) return;

        if (e.NewValue is string { Length: > 0 } key) text.SetResourceReference(TextBlock.ForegroundProperty, key);
        else text.ClearValue(TextBlock.ForegroundProperty);
    }
}
