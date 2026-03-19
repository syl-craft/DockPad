using System.Windows;
using System.Windows.Media;

namespace DockPad;

public enum AppDialogType { Info, Warning, Error, Confirm }

public partial class AppDialog : Window
{
    public bool Result { get; private set; }

    private AppDialog(string title, string message, AppDialogType type, bool isConfirm)
    {
        InitializeComponent();

        TitleText.Text   = title;
        MessageText.Text = message;

        var (hex, icon, primaryStyle) = type switch
        {
            AppDialogType.Error   => ("#D13438", "✕", "DangerButton"),
            AppDialogType.Warning => ("#D17B00", "⚠", "PrimaryButton"),
            AppDialogType.Confirm => ("#D13438", "!", "DangerButton"),
            _                     => ("#0078D4", "i", "PrimaryButton"),
        };

        var color = (Color)ColorConverter.ConvertFromString(hex);
        var brush = new SolidColorBrush(color);

        AccentBar.Background  = brush;
        IconBadge.Background  = brush;
        IconText.Text         = icon;

        BtnPrimary.Style = (Style)FindResource(primaryStyle);

        if (isConfirm)
        {
            BtnPrimary.Content            = "Oui";
            BtnSecondary.Visibility       = Visibility.Visible;
        }

        // Fermeture avec Echap
        KeyDown += (_, e) =>
        {
            if (e.Key == System.Windows.Input.Key.Escape) { Result = false; Close(); }
            if (e.Key == System.Windows.Input.Key.Enter)  { Result = true;  Close(); }
        };
    }

    private void BtnPrimary_Click(object sender, RoutedEventArgs e)   { Result = true;  Close(); }
    private void BtnSecondary_Click(object sender, RoutedEventArgs e) { Result = false; Close(); }

    // ── API statique ────────────────────────────────────────────────────────────

    public static bool Confirm(string message, string title = "Confirmation", Window? owner = null)
    {
        var dlg = new AppDialog(title, message, AppDialogType.Confirm, isConfirm: true);
        CenterOn(dlg, owner);
        dlg.ShowDialog();
        return dlg.Result;
    }

    public static void Error(string message, string title = "Erreur", Window? owner = null)
    {
        var dlg = new AppDialog(title, message, AppDialogType.Error, isConfirm: false);
        CenterOn(dlg, owner);
        dlg.ShowDialog();
    }

    public static void Warning(string message, string title = "Attention", Window? owner = null)
    {
        var dlg = new AppDialog(title, message, AppDialogType.Warning, isConfirm: false);
        CenterOn(dlg, owner);
        dlg.ShowDialog();
    }

    public static void Info(string message, string title = "Information", Window? owner = null)
    {
        var dlg = new AppDialog(title, message, AppDialogType.Info, isConfirm: false);
        CenterOn(dlg, owner);
        dlg.ShowDialog();
    }

    private static void CenterOn(AppDialog dlg, Window? owner)
    {
        if (owner != null)
        {
            dlg.Owner = owner;
            dlg.WindowStartupLocation = WindowStartupLocation.CenterOwner;
        }
    }
}
