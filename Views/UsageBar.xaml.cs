using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using DockPad.Services.Usage;

namespace DockPad.Views;

/// <summary>
/// Barre Usage IA à droite de la grille. Ne contient aucun calcul : tout vient de <see cref="UsageBarViewModel"/>,
/// testé sans WPF. Elle traduit les gestes (clic sur un logo ou une ligne, molette, clic droit) en appels au modèle.
/// </summary>
public partial class UsageBar : UserControl
{
    public UsageBar()
    {
        InitializeComponent();

        // Les boutons des gabarits n'ont pas de gestionnaire propre : un dictionnaire de ressources n'a pas de
        // code-behind. Le clic remonte jusqu'ici, où le modèle du bouton dit ce qu'il faut faire.
        AddHandler(Button.ClickEvent, new RoutedEventHandler(OnButtonClick));
    }

    /// <summary>
    /// L'état « aucun quota à afficher » renvoie aux réglages Usage IA.
    /// </summary>
    public event EventHandler? SettingsRequested;

    /// <summary>Modèle affiché. Posé par la fenêtre hôte, ou par l'outil de capture.</summary>
    public UsageBarViewModel? ViewModel
    {
        get => DataContext as UsageBarViewModel;
        set => DataContext = value;
    }

    /// <summary>
    /// La molette, n'importe où sur la barre : fournisseur suivant vers le bas, précédent vers le haut.
    /// </summary>
    protected override void OnPreviewMouseWheel(MouseWheelEventArgs e)
    {
        base.OnPreviewMouseWheel(e);
        if (ViewModel is not { } viewModel) return;

        viewModel.Cycle(e.Delta < 0 ? 1 : -1);
        e.Handled = true;
    }

    private void OnButtonClick(object sender, RoutedEventArgs e)
    {
        switch ((e.OriginalSource as FrameworkElement)?.DataContext)
        {
            case BarLogo logo:
                ViewModel?.Pick(logo.ProviderId);
                break;
            case BarHeroRow row:
                ViewModel?.Pick(row.ProviderId);
                break;
            case UsageBarViewModel:
                SettingsRequested?.Invoke(this, EventArgs.Empty);
                break;
        }
    }

    /// <summary>
    /// Le menu d'un logo, construit à l'ouverture : « Ouvrir la page de consommation », absent sans adresse.
    /// </summary>
    private void Logo_ContextMenuOpening(object sender, ContextMenuEventArgs e)
    {
        if (sender is not Button { DataContext: BarLogo logo, ContextMenu: { } menu }) return;

        if (UsagePageLink.IsWebUrl(logo.UsageUrl, out _) == false)
        {
            e.Handled = true;
            return;
        }

        var open = new MenuItem { Header = Loc.T("UsageBar_OpenPage") };
        open.Click += (_, _) => UsagePageLink.Open(logo.UsageUrl);
        menu.Items.Clear();
        menu.Items.Add(open);
    }
}
