using DockPad.Views;

namespace DockPad.Tests;

/// <summary>
/// Les commandes de la fenêtre d'accès rapide, vérifiées sans WPF.
/// </summary>
/// <remarks>
/// C'est tout l'intérêt de l'interface <c>IQuickAccessView</c> : une vue factice suffit à vérifier
/// qu'une commande appelle bien ce qu'elle prétend. Avec vingt gestionnaires <c>Click</c>, la seule
/// vérification possible était de cliquer dans l'application.
/// </remarks>
public class QuickAccessCommandsTests
{
    private sealed class FakeView : IQuickAccessView
    {
        public List<string> Calls { get; } = [];
        public string? LastMessage { get; private set; }
        public string? LastPath { get; private set; }

        public void ShowContextMenuManager() => Calls.Add(nameof(ShowContextMenuManager));
        public void ShowPresets() => Calls.Add(nameof(ShowPresets));
        public void ShowSettings() => Calls.Add(nameof(ShowSettings));
        public void ShowBrowsers() => Calls.Add(nameof(ShowBrowsers));
        public void ShowMcpConfig() => Calls.Add(nameof(ShowMcpConfig));
        public void ShowUsageConfig() => Calls.Add(nameof(ShowUsageConfig));
        public void RefreshGrid() => Calls.Add(nameof(RefreshGrid));
        public void ToggleTileLock() => Calls.Add(nameof(ToggleTileLock));
        public void Minimize() => Calls.Add(nameof(Minimize));
        public void HideToTray() => Calls.Add(nameof(HideToTray));
        public void Quit() => Calls.Add(nameof(Quit));
        public void OpenPath(string path) { Calls.Add(nameof(OpenPath)); LastPath = path; }
        public void ShowInfo(string message) { Calls.Add(nameof(ShowInfo)); LastMessage = message; }
    }

    [Fact]
    public void ChaqueCommandeAppelleLeGesteQuElleAnnonce()
    {
        var view = new FakeView();
        var commands = new QuickAccessCommands(view);

        commands.OpenSettings.Execute(null);
        commands.OpenBrowsers.Execute(null);
        commands.OpenMcpConfig.Execute(null);
        commands.OpenUsageConfig.Execute(null);
        commands.OpenPresets.Execute(null);
        commands.OpenContextMenuManager.Execute(null);
        commands.Refresh.Execute(null);
        commands.ToggleTileLock.Execute(null);
        commands.Minimize.Execute(null);
        commands.HideToTray.Execute(null);
        commands.Quit.Execute(null);

        Assert.Equal(
        [
            "ShowSettings", "ShowBrowsers", "ShowMcpConfig", "ShowUsageConfig", "ShowPresets",
            "ShowContextMenuManager", "RefreshGrid", "ToggleTileLock", "Minimize", "HideToTray",
            "Quit",
        ], view.Calls);
    }

    [Fact]
    public void ModifierLaConfiguration_OuvreLeFichierDesRaccourcis()
    {
        // Et non le dossier : « ✎ Modifier » édite shortcuts.json, « 📁 Voir le dossier » ouvre le
        // profil. Les deux commandes se ressemblent assez pour être interverties un jour.
        var view = new FakeView();

        new QuickAccessCommands(view).EditConfig.Execute(null);

        Assert.EndsWith("shortcuts.json", view.LastPath);
    }

    [Fact]
    public void VoirLeDossier_OuvreLeProfil()
    {
        var view = new FakeView();

        new QuickAccessCommands(view).OpenConfigFolder.Execute(null);

        Assert.DoesNotContain(".json", view.LastPath);
    }

    [Fact]
    public void Sauvegarder_AnnonceLeDossierDeSauvegarde()
    {
        var view = new FakeView();

        new QuickAccessCommands(view).BackupConfig.Execute(null);

        Assert.Contains("ShowInfo", view.Calls);
        Assert.Contains(".backup", view.LastMessage);
    }

    [Fact]
    public void ToutesLesCommandesSontExecutablesParDefaut()
    {
        // Aucune n'a de condition aujourd'hui ; le jour où l'une en gagne une, ce test le signalera
        // et forcera à décrire la condition ici.
        var commands = new QuickAccessCommands(new FakeView());

        Assert.True(commands.Refresh.CanExecute(null));
        Assert.True(commands.BackupConfig.CanExecute(null));
        Assert.True(commands.Quit.CanExecute(null));
    }
}
