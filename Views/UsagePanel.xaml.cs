using System.ComponentModel;
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
            if (_viewModel is not null) _viewModel.PropertyChanged -= OnViewModelChanged;
            _viewModel = value;
            DataContext = value;
            if (_viewModel is not null) _viewModel.PropertyChanged += OnViewModelChanged;
            SyncVisibility();
        }
    }

    private void OnViewModelChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(UsageViewModel.IsVisible)) SyncVisibility();
    }

    /// <summary>
    /// Replie le contrôle entier, et non son seul contenu.
    /// </summary>
    /// <remarks>
    /// La fenêtre hôte pose une largeur et une hauteur explicites sur ce contrôle pour l'aligner sur
    /// les tuiles. Masquer le <c>Border</c> intérieur laissait donc ces 90 px et leur marge occuper
    /// la place : un grand vide sous la grille quand le bandeau est désactivé.
    /// </remarks>
    private void SyncVisibility() =>
        Visibility = _viewModel?.IsVisible == true ? Visibility.Visible : Visibility.Collapsed;

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
