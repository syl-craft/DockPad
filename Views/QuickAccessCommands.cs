using System.Windows.Input;
using DockPad.Services;
using DockPad.Services.Localization;

namespace DockPad.Views;

/// <summary>
/// Ce que la fenêtre d'accès rapide sait faire, exposé en commandes.
/// </summary>
/// <remarks>
/// <para>
/// Avant, ces actions étaient vingt gestionnaires <c>Click</c> dispersés dans un fichier de
/// 1 500 lignes : rien ne les listait, et aucune n'était testable. Ici elles sont énumérées en un
/// endroit, et celles qui portent une décision — la sauvegarde de configuration — se vérifient avec
/// une vue factice.
/// </para>
/// <para>
/// <b>Pourquoi une interface plutôt que la fenêtre elle-même.</b> Ouvrir un dialogue demande un
/// <c>Owner</c>, réduire demande une fenêtre : ce sont des gestes de vue, pas des décisions. La
/// commande décide et appelle ; <see cref="IQuickAccessView"/> exécute. C'est ce qui permet de
/// tester la logique sans WPF.
/// </para>
/// </remarks>
public sealed class QuickAccessCommands(IQuickAccessView view)
{
    // ── Menu contextuel Windows
    public ICommand OpenContextMenuManager { get; } = new RelayCommand(view.ShowContextMenuManager);
    public ICommand OpenPresets { get; } = new RelayCommand(view.ShowPresets);

    // ── Paramètres
    public ICommand OpenSettings { get; } = new RelayCommand(view.ShowSettings);
    public ICommand OpenBrowsers { get; } = new RelayCommand(view.ShowBrowsers);
    public ICommand OpenMcpConfig { get; } = new RelayCommand(view.ShowMcpConfig);
    public ICommand OpenSecretSettings { get; } = new RelayCommand(view.ShowSecretSettings);
    public ICommand SyncVault { get; } = new RelayCommand(view.SyncVault);
    public ICommand OpenUsageConfig { get; } = new RelayCommand(view.ShowUsageConfig);

    // ── Configuration
    public ICommand Refresh { get; } = new RelayCommand(view.RefreshGrid);
    public ICommand EditConfig { get; } = new RelayCommand(() => view.OpenPath(ShortcutService.FilePath));
    public ICommand OpenConfigFolder { get; } =
        new RelayCommand(() => view.OpenPath(AppPaths.ProfileRoot));

    /// <summary>
    /// Sauvegarde les configurations et annonce où. La seule commande qui porte une vraie décision,
    /// et la seule qu'on peut donc tester de bout en bout.
    /// </summary>
    public ICommand BackupConfig { get; } = new RelayCommand(() =>
    {
        var dir = ConfigBackup.Run(AppPaths.ProfileRoot, ConfigBackup.ProfileFiles(), DateTime.Now);
        view.ShowInfo(Loc.F("Quick_BackupCreated", dir));
    });

    // ── Fenêtre
    public ICommand ToggleTileLock { get; } = new RelayCommand(view.ToggleTileLock);
    public ICommand Minimize { get; } = new RelayCommand(view.Minimize);
    public ICommand HideToTray { get; } = new RelayCommand(view.HideToTray);
    public ICommand Quit { get; } = new RelayCommand(view.Quit);
}

/// <summary>
/// Les gestes que seule la fenêtre peut faire : ouvrir un dialogue avec un propriétaire, se réduire,
/// afficher un message. Tout le reste appartient aux commandes.
/// </summary>
public interface IQuickAccessView
{
    void ShowContextMenuManager();
    void ShowPresets();
    void ShowSettings();
    void ShowBrowsers();
    void ShowMcpConfig();
    void ShowUsageConfig();

    /// <summary>Les Options, ouvertes directement sur l'onglet Secrets.</summary>
    void ShowSecretSettings();

    /// <summary>Rafraîchit le cache local de la CLI Bitwarden.</summary>
    void SyncVault();

    void RefreshGrid();
    void ToggleTileLock();

    void Minimize();
    void HideToTray();
    void Quit();

    /// <summary>Ouvre un chemin avec l'application par défaut — dossier ou fichier.</summary>
    void OpenPath(string path);

    void ShowInfo(string message);
}
