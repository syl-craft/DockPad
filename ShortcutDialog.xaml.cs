using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Imaging;
using WinContextMenuManager.Models;
using OpenFileDialog = Microsoft.Win32.OpenFileDialog;

namespace WinContextMenuManager;

public partial class ShortcutDialog : Window
{
    public ShortcutEntry Entry { get; private set; }

    public ShortcutDialog(ShortcutEntry? existing = null, int row = 0, int col = 0)
    {
        InitializeComponent();

        Entry = existing != null
            ? new ShortcutEntry
            {
                Row = existing.Row, Col = existing.Col,
                Name = existing.Name, Type = existing.Type,
                Command = existing.Command, IconPath = existing.IconPath
            }
            : new ShortcutEntry { Row = row, Col = col };

        TxtHeader.Text = existing != null ? "Modifier le raccourci" : "Nouveau raccourci";
        TxtName.Text = Entry.Name;
        TxtCommand.Text = Entry.Command;
        TxtIconPath.Text = Entry.IconPath;

        SetTypeCombo(Entry.Type);
        TxtIconPath.TextChanged += (_, _) => RefreshIconPreview();
        RefreshIconPreview();
    }

    private void SetTypeCombo(ShortcutType type)
    {
        foreach (ComboBoxItem item in CmbType.Items)
        {
            if (item.Tag?.ToString() == type.ToString())
            {
                CmbType.SelectedItem = item;
                return;
            }
        }
        CmbType.SelectedIndex = 0;
    }

    private void CmbType_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (LblCommand == null) return; // pas encore initialisé

        var tag = (CmbType.SelectedItem as ComboBoxItem)?.Tag?.ToString();

        LblCommand.Content = tag switch
        {
            "OpenFolder" or "OpenTerminal" => "Chemin du dossier *",
            "OpenUrl"                      => "URL *",
            _                              => "Commande *",
        };

        if (BtnBrowse != null)
            BtnBrowse.Visibility = tag == "OpenUrl" ? Visibility.Collapsed : Visibility.Visible;
    }

    private void BrowseCommand_Click(object sender, RoutedEventArgs e)
    {
        var tag = (CmbType.SelectedItem as ComboBoxItem)?.Tag?.ToString();

        if (tag is "OpenFolder" or "OpenTerminal")
        {
            var dlg = new System.Windows.Forms.FolderBrowserDialog
            {
                Description = "Choisir un dossier",
                UseDescriptionForTitle = true,
            };
            if (Directory.Exists(TxtCommand.Text))
                dlg.InitialDirectory = TxtCommand.Text;

            if (dlg.ShowDialog() == System.Windows.Forms.DialogResult.OK)
                TxtCommand.Text = dlg.SelectedPath;
        }
        else
        {
            var dlg = new OpenFileDialog
            {
                Title = "Choisir un exécutable",
                Filter = "Exécutables (*.exe)|*.exe|Tous les fichiers (*.*)|*.*"
            };
            if (dlg.ShowDialog() == true)
                TxtCommand.Text = dlg.FileName;
        }
    }

    private void BrowseIcon_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new OpenFileDialog
        {
            Title = "Choisir une icône",
            Filter = "Images et exécutables|*.png;*.ico;*.bmp;*.jpg;*.jpeg;*.exe;*.dll|Tous les fichiers|*.*"
        };
        if (File.Exists(TxtIconPath.Text))
            dlg.InitialDirectory = Path.GetDirectoryName(TxtIconPath.Text);

        if (dlg.ShowDialog() == true)
            TxtIconPath.Text = dlg.FileName;
    }

    private void RefreshIconPreview()
    {
        try
        {
            string path = TxtIconPath.Text.Trim().Split(',')[0].Trim('"');
            if (!File.Exists(path)) { ImgIconPreview.Source = null; return; }

            string ext = Path.GetExtension(path).ToLowerInvariant();
            if (ext is ".ico" or ".png" or ".bmp" or ".jpg" or ".jpeg")
            {
                ImgIconPreview.Source = new BitmapImage(new Uri(path));
                return;
            }
            if (ext is ".exe" or ".dll")
            {
                using var icon = System.Drawing.Icon.ExtractAssociatedIcon(path);
                if (icon is null) { ImgIconPreview.Source = null; return; }
                using var bmp = icon.ToBitmap();
                var handle = bmp.GetHbitmap();
                try
                {
                    ImgIconPreview.Source = System.Windows.Interop.Imaging.CreateBitmapSourceFromHBitmap(
                        handle, IntPtr.Zero, Int32Rect.Empty,
                        BitmapSizeOptions.FromEmptyOptions());
                }
                finally { DeleteObject(handle); }
                return;
            }
        }
        catch { }
        ImgIconPreview.Source = null;
    }

    [System.Runtime.InteropServices.DllImport("gdi32.dll")]
    private static extern bool DeleteObject(IntPtr hObject);

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        string name    = TxtName.Text.Trim();
        string command = TxtCommand.Text.Trim();

        if (string.IsNullOrWhiteSpace(name))
        {
            MessageBox.Show("Le nom est obligatoire.", "Validation",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            TxtName.Focus();
            return;
        }
        if (string.IsNullOrWhiteSpace(command))
        {
            MessageBox.Show("La commande / le chemin est obligatoire.", "Validation",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            TxtCommand.Focus();
            return;
        }

        ShortcutType type = ShortcutType.RunCommand;
        if (CmbType.SelectedItem is ComboBoxItem item)
            Enum.TryParse(item.Tag?.ToString(), out type);

        Entry.Name     = name;
        Entry.Type     = type;
        Entry.Command  = command;
        Entry.IconPath = TxtIconPath.Text.Trim();

        DialogResult = true;
        Close();
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }
}
