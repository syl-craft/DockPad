using System.Diagnostics;
using System.Reflection;
using System.Windows;
using DockPad;
using DockPad.Models;
using DockPad.Services;

namespace DialogShot;

/// <summary>
/// Chronomètre un peuplement de la grille, sur une page pleine de tuiles de dossier.
/// </summary>
/// <remarks>
/// C'est le geste le plus fréquent de la fenêtre : chaque changement de page le refait, et chaque
/// changement de langue aussi. Les tuiles de dossier sont le cas coûteux — leur menu contextuel lit
/// <c>Directory\Background\shell</c> dans le registre.
/// </remarks>
internal static class GridBench
{
    public static void Run(bool folders)
    {
        var entries = new List<ShortcutEntry>();
        for (int row = 0; row < 4; row++)
            for (int col = 0; col < 6; col++)
                entries.Add(new ShortcutEntry
                {
                    Page = 0, Row = row, Col = col,
                    Name = $"Dossier {row}{col}",
                    Type = folders ? ShortcutType.OpenFolder : ShortcutType.RunCommand,
                    Command = folders ? @"C:\dev" : "notepad.exe",
                });
        ShortcutService.Save(entries);

        var window = new QuickAccessWindow();
        var content = (FrameworkElement)window.Content;
        BindingCheck.ForceLayout(content, window.Width);

        var populate = typeof(QuickAccessWindow)
            .GetMethod("PopulateGrid", BindingFlags.NonPublic | BindingFlags.Instance)!;

        for (int i = 0; i < 3; i++) { populate.Invoke(window, null); BindingCheck.ForceLayout(content, window.Width); }

        const int runs = 20;
        var watch = Stopwatch.StartNew();
        for (int i = 0; i < runs; i++) { populate.Invoke(window, null); BindingCheck.ForceLayout(content, window.Width); }
        watch.Stop();

        Console.WriteLine($"  24 tuiles {(folders ? "de dossier" : "de commande")}, {runs} peuplements : "
                          + $"{watch.Elapsed.TotalMilliseconds / runs:F1} ms par peuplement");
    }
}
