using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using DockPad.Models;

namespace DockPad.Views;

/// <summary>
/// Une case de la grille : une tuile, ou un emplacement libre.
/// </summary>
/// <remarks>
/// <para>
/// La grille était construite bouton par bouton en code — vingt-quatre <c>Button</c> fabriqués, avec
/// leurs neuf gestionnaires et leur menu contextuel, à chaque changement de page ou de langue. Elle
/// est désormais décrite en XAML et alimentée par ces cellules : ce qui s'affiche est une
/// conséquence des données, plus une suite d'instructions.
/// </para>
/// <para>
/// La cellule porte ce que le gabarit affiche — nom, icône, infobulle, couleur de bande — et
/// <see cref="Entry"/> pour les gestes qui ont besoin du raccourci lui-même. <c>Row</c> et
/// <c>Col</c> restent nécessaires : une case libre n'a pas d'entrée, mais l'ajout doit savoir où.
/// </para>
/// </remarks>
public sealed class TileCell
{
    public required int Row { get; init; }
    public required int Col { get; init; }

    /// <summary>Le raccourci, ou <c>null</c> pour une case libre.</summary>
    public ShortcutEntry? Entry { get; init; }

    public bool IsEmpty => Entry is null;

    public string Name => Entry?.Name ?? "";
    public ImageSource? Icon { get; init; }
    public string Tooltip { get; init; } = "";

    /// <summary>Couleur de la bande de type, lue par le gabarit et par le survol.</summary>
    public Brush? Band { get; init; }
}

/// <summary>
/// Choisit le gabarit d'une case : une tuile ou un emplacement libre.
/// </summary>
/// <remarks>
/// Deux gabarits plutôt qu'un seul avec déclencheurs : leurs contenus n'ont rien en commun — une
/// icône et un libellé d'un côté, un « + » de l'autre — et un gabarit unique qui échangerait tout
/// son contenu serait moins lisible que deux gabarits nommés.
/// </remarks>
public sealed class TileTemplateSelector : DataTemplateSelector
{
    public DataTemplate? Tile { get; set; }
    public DataTemplate? Empty { get; set; }

    public override DataTemplate? SelectTemplate(object item, DependencyObject container) =>
        item is TileCell { IsEmpty: true } ? Empty : Tile;
}
