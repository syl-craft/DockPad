using System.Windows;
using System.Windows.Controls;
using DockPad.Services.Usage;

namespace DockPad.Views;

public partial class UsageHoverCardView : UserControl
{
    public UsageHoverCardView()
    {
        InitializeComponent();
        // Le Popup créé en code est hors de l'arbre logique : ses ressources doivent suivre celles de l'application.
        if (Application.Current is { } app) Resources.MergedDictionaries.Add(app.Resources);
    }

    internal bool FocusLink() => PageLink.IsVisible && PageLink.Focus();

    private void OpenPage_Click(object sender, RoutedEventArgs e)
    {
        e.Handled = true;
        if (DataContext is UsageHoverCard card) UsagePageLink.Open(card.UsageUrl);
    }
}
