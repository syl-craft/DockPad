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

    private void Tab_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: UsageTabItem tab })
        {
            _viewModel?.Select(tab.ProviderId);
        }
    }
}
