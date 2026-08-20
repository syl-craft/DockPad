using System.Diagnostics;
using System.Windows;
using DockPad.Models;
using DockPad.Services;
using DockPad.Services.Usage;

namespace DockPad.Views;

/// <summary>
/// Bandeau de consommation IA. Ne contient aucun calcul : tout vient de
/// <see cref="UsageViewModel"/>, qui est testé sans WPF.
/// </summary>
public partial class UsagePanel : UserControl
{
    private UsageViewModel? _viewModel;

    public UsagePanel()
    {
        InitializeComponent();
    }

    /// <summary>ViewModel affiché. Posé par la fenêtre hôte, ou par l'outil de capture.</summary>
    public UsageViewModel? ViewModel
    {
        get => _viewModel;
        set
        {
            _viewModel = value;
            DataContext = value;
        }
    }

    /// <summary>Démarre le rafraîchissement. À appeler quand la fenêtre hôte s'affiche.</summary>
    public void Start() => _viewModel?.Start();

    /// <summary>Arrête le rafraîchissement. À appeler quand la fenêtre hôte se masque.</summary>
    public void Stop() => _viewModel?.Stop();

    /// <summary>
    /// Ouvre la page de consommation du fournisseur dans le navigateur par défaut.
    /// </summary>
    /// <remarks>
    /// Le schéma est vérifié avant le lancement. L'URL vient aujourd'hui d'une constante dans le
    /// code du fournisseur, mais <c>Process.Start</c> avec <c>UseShellExecute</c> exécuterait aussi
    /// bien un chemin de fichier ou une commande : la garde évite qu'un futur fournisseur qui lirait
    /// son URL ailleurs n'ouvre autre chose qu'une page web.
    /// </remarks>
    private void OpenUsagePage_Click(object sender, RoutedEventArgs e)
    {
        var url = _viewModel?.UsageUrl ?? "";
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri)) return;
        if (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps) return;

        try
        {
            Process.Start(new ProcessStartInfo(uri.AbsoluteUri) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            LogService.Warn(ex, "Ouverture de la page de consommation du fournisseur");
        }
    }

    private void Tab_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: UsageTabItem tab })
        {
            _viewModel?.Select(tab.ProviderId);
        }
    }
}
