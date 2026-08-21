using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;

namespace DockPad;

/// <summary>
/// Couleur exprimée en chaîne (« #34A853 ») vers un <see cref="Brush"/>.
/// </summary>
/// <remarks>
/// Les modèles d'affichage du bandeau Usage IA portent leurs couleurs en texte : la décision de
/// couleur appartient à la logique (seuil d'alerte, accent du fournisseur), qui est testée sans
/// WPF — un <see cref="Brush"/> y traînerait une dépendance à la présentation.
/// </remarks>
public class StringToBrushConverter : IValueConverter
{
    private static readonly BrushConverter Inner = new();

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is string text && text.Length > 0)
        {
            try
            {
                if (Inner.ConvertFromString(text) is Brush brush) return brush;
            }
            catch (FormatException)
            {
                // Couleur illisible : on retombe sur le gris plutôt que de faire tomber le rendu.
            }
        }
        return Brushes.Gray;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
