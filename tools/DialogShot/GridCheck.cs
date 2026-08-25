using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using Button = System.Windows.Controls.Button;
using System.Windows.Media;
using DockPad;

namespace DialogShot;

/// <summary>
/// Vérifie le câblage de la grille de tuiles.
/// </summary>
/// <remarks>
/// <para>
/// Le passage d'une grille construite en code à un gabarit déclaratif a cassé trois choses qui
/// <b>compilaient parfaitement</b> et que la comparaison pixel ne montrait pas : le
/// <c>DataContext</c> d'un bouton de gabarit est la cellule et non le raccourci, et un bouton
/// d'<c>ItemsControl</c> n'a plus de <c>Grid.Row</c> — tout dépôt atterrissait donc en (0,0).
/// </para>
/// <para>
/// Ce contrôle vérifie ce que ces gestionnaires lisent : chaque case rendue porte bien une cellule,
/// et sa position correspond à sa place dans la grille.
/// </para>
/// </remarks>
internal static class GridCheck
{
    public static int Run()
    {
        // Des tuiles a des positions CHOISIES : sans elles le controle passerait a vide, et les
        // assertions qui comptent — la cellule que lisent le clic et le depot — ne seraient jamais
        // exercees.
        WriteFixture();

        var window = new QuickAccessWindow();
        BindingCheck.ForceLayout((FrameworkElement)window.Content, window.Width);

        var grid = FindByName(window, "ShortcutsGrid") as ItemsControl;
        if (grid is null) { Console.WriteLine("ECHEC : ShortcutsGrid introuvable"); return 2; }

        var problems = new List<string>();

        if (grid.Items.Count != 24)
            problems.Add($"24 cases attendues, {grid.Items.Count} rendues");

        var occupied = 0;
        for (var i = 0; i < grid.Items.Count; i++)
        {
            var expectedRow = i / 6;
            var expectedCol = i % 6;

            if (grid.ItemContainerGenerator.ContainerFromIndex(i) is not ContentPresenter presenter)
            {
                problems.Add($"case {i} : aucun conteneur genere");
                continue;
            }

            var button = Descendant<Button>(presenter);
            if (button is null) { problems.Add($"case {i} : aucun bouton"); continue; }

            // C'est exactement ce que lisent Tile_Click, TileDrag_MouseMove et TileDrop_Drop.
            if (button.DataContext is not DockPad.Views.TileCell cell)
            {
                problems.Add($"case {i} : DataContext = {button.DataContext?.GetType().Name ?? "null"}, "
                             + "attendu TileCell — clic, glissement et depot seraient inertes");
                continue;
            }

            if (cell.Row != expectedRow || cell.Col != expectedCol)
                problems.Add($"case {i} : cellule ({cell.Row},{cell.Col}), attendu "
                             + $"({expectedRow},{expectedCol}) — un depot atterrirait ailleurs");

            // Le menu doit exister DES LA CONSTRUCTION : WPF ne leve ContextMenuOpening que sur un
            // element qui en porte deja un. Sans lui, le clic droit sur une tuile ne montre rien.
            if (button.ContextMenu is not { } menu)
            {
                problems.Add($"case {i} : aucun menu contextuel — ContextMenuOpening ne serait jamais leve");
            }
            else
            {
                var handler = cell.IsEmpty ? "EmptyTile_ContextMenuOpening" : "Tile_ContextMenuOpening";
                typeof(QuickAccessWindow)
                    .GetMethod(handler, BindingFlags.NonPublic | BindingFlags.Instance)!
                    .Invoke(window, [button, null]);

                var expected = cell.IsEmpty ? 1 : 7; // libre : Ajouter ; occupee : 5 entrees + 2 separateurs
                if (menu.Items.Count < expected)
                    problems.Add($"case {i} : menu a {menu.Items.Count} entree(s), au moins {expected} attendue(s)");
            }

            if (!cell.IsEmpty)
            {
                occupied++;
                if (cell.Entry!.Row != cell.Row || cell.Entry.Col != cell.Col)
                    problems.Add($"case {i} : le raccourci se croit en ({cell.Entry.Row},{cell.Entry.Col})");
            }
        }

        Console.WriteLine($"  {grid.Items.Count} cases, dont {occupied} occupees");

        if (occupied != 3)
            problems.Add($"3 tuiles posees dans la fixture, {occupied} rendues");

        if (problems.Count == 0)
        {
            Console.WriteLine("grille : cablage correct");
            return 0;
        }

        Console.WriteLine($"{problems.Count} probleme(s) :");
        foreach (var problem in problems) Console.WriteLine("  " + problem);
        return 1;
    }

    /// <summary>Trois tuiles aux coins et au milieu : de quoi verifier la correspondance des cases.</summary>
    internal static void WriteFixture()
    {
        DockPad.Services.ShortcutService.Save(
        [
            new DockPad.Models.ShortcutEntry { Page = 0, Row = 0, Col = 0, Name = "Premiere",
                                               Type = DockPad.Models.ShortcutType.RunCommand, Command = "a.exe" },
            new DockPad.Models.ShortcutEntry { Page = 0, Row = 1, Col = 3, Name = "Milieu",
                                               Type = DockPad.Models.ShortcutType.OpenFolder, Command = @"C:\dev" },
            new DockPad.Models.ShortcutEntry { Page = 0, Row = 3, Col = 5, Name = "Derniere",
                                               Type = DockPad.Models.ShortcutType.OpenUrl, Command = "https://exemple.fr" },
        ]);
    }

    private static object? FindByName(DependencyObject root, string name) =>
        root is FrameworkElement element ? element.FindName(name) : null;

    private static T? Descendant<T>(DependencyObject parent) where T : DependencyObject
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            if (child is T match) return match;

            if (Descendant<T>(child) is { } deeper) return deeper;
        }
        return null;
    }
}
