using System.ComponentModel;
using System.Windows;
using DockPad.Models;
using DockPad.Services.Usage;

namespace DockPad.Views;

/// <summary>
/// Bandeau de consommation IA. Ne contient aucun calcul : tout vient de
/// <see cref="UsageViewModel"/>, qui est testé sans WPF.
/// </summary>
public partial class UsagePanel : UserControl
{
    private UsageViewModel? _viewModel;
    private bool _isFolded;

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
    /// Replié quand la barre à droite remplace le bandeau. Le modèle reste le même : changer de position ne relit rien.
    /// </summary>
    public bool IsFolded
    {
        get => _isFolded;
        set
        {
            _isFolded = value;
            SyncVisibility();
        }
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
        Visibility = _viewModel?.IsVisible == true && _isFolded == false ? Visibility.Visible : Visibility.Collapsed;

    /// <summary>Démarre le rafraîchissement. À appeler quand la fenêtre hôte s'affiche.</summary>
    public void Start() => _viewModel?.Start();

    /// <summary>Arrête le rafraîchissement. À appeler quand la fenêtre hôte se masque.</summary>
    public void Stop() => _viewModel?.Stop();

    /// <summary>
    /// Ouvre la page de consommation du fournisseur dans le navigateur par défaut, après le contrôle du schéma.
    /// </summary>
    private void OpenUsagePage_Click(object sender, RoutedEventArgs e) =>
        UsagePageLink.Open(_viewModel?.UsageUrl);

    private void Tab_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: UsageTabItem tab })
        {
            _viewModel?.SelectByGesture(tab.ProviderId);
        }
    }
}
