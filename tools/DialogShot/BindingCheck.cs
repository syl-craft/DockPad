using System.Windows;
using System.Windows.Controls;
using ButtonBase = System.Windows.Controls.Primitives.ButtonBase;
using System.Windows.Data;
using System.Windows.Media;

namespace DialogShot;

/// <summary>
/// Détecte les liaisons <c>Command</c> qui échouent en silence.
/// </summary>
/// <remarks>
/// <para>
/// Une liaison cassée — mauvais <c>DataContext</c>, propriété renommée, faute de frappe — ne fait
/// <b>ni erreur de compilation ni exception</b> : WPF laisse la propriété à <c>null</c> et le
/// contrôle reste là, cliquable, sans rien faire. C'est le risque exact de la migration des
/// <c>Click</c> vers des commandes.
/// </para>
/// <para>
/// <b>Pourquoi pas la trace de débogage de WPF.</b> Première tentative : écouter
/// <c>PresentationTraceSources.DataBindingSource</c>. Elle ne voit rien, parce qu'une liaison ne
/// s'évalue qu'au moment où le contrôle se met en page — or les commandes migrées vivent dans un
/// <c>ContextMenu</c>, qui n'entre dans aucun arbre visuel tant qu'il n'est pas ouvert. Une
/// mutation volontaire l'a prouvé : le détecteur annonçait « aucune liaison cassée » sur une
/// liaison délibérément fautive.
/// </para>
/// <para>
/// La méthode retenue lit la liaison <b>directement</b> : parcourir l'arbre logique — qui, lui,
/// contient les menus — et, pour chaque contrôle dont la propriété <c>Command</c> porte une liaison,
/// constater qu'elle rend bien une commande. Lire la propriété force l'évaluation, sans avoir à
/// ouvrir le moindre popup.
/// </para>
/// </remarks>
internal static class BindingCheck
{
    /// <summary>Contrôles dont la liaison <c>Command</c> ne rend rien.</summary>
    public static List<string> BrokenCommands(DependencyObject root)
    {
        var broken = new List<string>();

        foreach (var element in Descendants(root))
        {
            DependencyProperty property;
            System.Windows.Input.ICommand? value;

            switch (element)
            {
                case MenuItem item:
                    property = MenuItem.CommandProperty;
                    value = item.Command;
                    break;
                case ButtonBase button:
                    property = ButtonBase.CommandProperty;
                    value = button.Command;
                    break;
                default:
                    continue;
            }

            // Pas de liaison déclarée : le contrôle utilise un gestionnaire d'événement, très bien.
            if (BindingOperations.GetBinding(element, property) is not { } binding) continue;

            // Lire la propriété a forcé l'évaluation ; null = la liaison n'a rien trouvé.
            if (value is null)
                broken.Add($"{Describe(element)} → {{Binding {binding.Path.Path}}}");
        }

        return broken;
    }

    /// <summary>
    /// Arbre logique, plus les menus contextuels attachés — ceux-ci ne sont ni enfants visuels ni
    /// enfants logiques, mais une propriété.
    /// </summary>
    private static IEnumerable<DependencyObject> Descendants(DependencyObject root)
    {
        yield return root;

        if (root is FrameworkElement { ContextMenu: { } menu })
            foreach (var descendant in Descendants(menu))
                yield return descendant;

        foreach (var child in LogicalTreeHelper.GetChildren(root).OfType<DependencyObject>())
            foreach (var descendant in Descendants(child))
                yield return descendant;
    }

    /// <summary>Un nom lisible pour le rapport : l'en-tête d'un menu, le contenu d'un bouton.</summary>
    private static string Describe(DependencyObject element) => element switch
    {
        MenuItem { Header: string header } => $"MenuItem « {header} »",
        ButtonBase { Content: string content } => $"Button « {content} »",
        _ => element.GetType().Name,
    };

    /// <summary>Met en page l'arbre : certaines liaisons ne s'évaluent qu'à la mesure.</summary>
    public static void ForceLayout(FrameworkElement root, double width)
    {
        root.Measure(new System.Windows.Size(width, double.PositiveInfinity));
        root.Arrange(new Rect(0, 0, width, root.DesiredSize.Height));
        root.UpdateLayout();
    }
}
