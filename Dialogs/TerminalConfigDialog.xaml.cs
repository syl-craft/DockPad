using System.IO;
using System.Windows;
using DockPad.Models;
using DockPad.Services;
using Microsoft.Win32;

namespace DockPad;

/// <summary>
/// Les terminaux de DockPad : liste (détection + ajout), visibilité, ordre et arguments, sur le
/// patron de la fenêtre Navigateurs. Sauvegarde immédiate à chaque action.
/// </summary>
public partial class TerminalConfigDialog : Window
{
    private readonly TerminalsConfig _config;

    private sealed record TerminalItem(TerminalEntry Entry, System.Windows.Media.ImageSource? Icon,
                                       string Name, string Detail, string Badges, bool Visible);

    /// <summary>
    /// Vrai pendant que la sélection remplit les champs : ce n'est pas une saisie.
    /// </summary>
    private bool _showing;

    public TerminalConfigDialog()
    {
        InitializeComponent();
        TxtVersion.Text = AppInfo.VersionText;
        _config = TerminalConfigService.EnsureInitialized();
        RefreshList();
    }

    private TerminalEntry? Selected => (LstTerminals.SelectedItem as TerminalItem)?.Entry;

    private void Save() => TerminalConfigService.Save(_config);

    private void RefreshList(string? selectId = null)
    {
        string defaultId = SettingsService.LoadDefaultTerminal();
        var rows = _config.Terminals.OrderBy(t => t.Order).ToList();

        LstTerminals.ItemsSource = rows.Select(t => new TerminalItem(
            t,
            IconStoreService.LoadImage(Environment.ExpandEnvironmentVariables(t.ExePath)),
            t.Name,
            t.ExePath,
            Badges(t, defaultId),
            !t.Hidden)).ToList();

        if (selectId != null)
            LstTerminals.SelectedIndex = rows.FindIndex(t => t.Id == selectId);
    }

    private static string Badges(TerminalEntry terminal, string defaultId)
    {
        var badges = new List<string>();
        if (terminal.Id == defaultId) badges.Add(Loc.T("Terminals_Badge_Default"));
        if (terminal.Hidden) badges.Add(Loc.T("Terminals_Badge_Hidden"));
        if (!TerminalDetectionService.ProgramExists(terminal.ExePath)) badges.Add(Loc.T("Terminals_Badge_Missing"));
        return string.Join(" · ", badges);
    }

    // ── Modification ────────────────────────────────────────────────────────────

    private void LstTerminals_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        var terminal = Selected;

        _showing = true;
        TxtName.Text        = terminal?.Name ?? "";
        TxtExe.Text         = terminal?.ExePath ?? "";
        TxtFolderArgs.Text  = terminal?.FolderArgs ?? "";
        TxtCommandArgs.Text = terminal?.CommandArgs ?? "";
        _showing = false;

        PnlEdit.IsEnabled = terminal != null;
        RefreshPreview();
    }

    /// <summary>
    /// Le terminal tel que le décrivent les champs, appliqué ou non.
    /// </summary>
    private TerminalInfo EditedTerminal() => TerminalCatalog.ToInfo(new TerminalEntry
    {
        Id          = Selected?.Id ?? "",
        Name        = TxtName.Text,
        ExePath     = TxtExe.Text,
        FolderArgs  = TxtFolderArgs.Text,
        CommandArgs = TxtCommandArgs.Text,
    });

    private void Field_Changed(object sender, System.Windows.Controls.TextChangedEventArgs e)
    {
        if (!_showing) RefreshPreview();
    }

    /// <summary>
    /// La commande finale du prédéfini Claude avec les champs en cours, et ce qui empêcherait de la lancer.
    /// </summary>
    private void RefreshPreview()
    {
        if (TxtPreview == null) return;

        var terminal = EditedTerminal();
        bool exists = Selected != null && TerminalDetectionService.ProgramExists(terminal.ExePath);
        bool runsCommand = terminal.CommandArgs.Contains(TerminalDetectionService.CommandToken)
                           || terminal.CommandArgs.Contains(TerminalDetectionService.PowerShellCommandToken);

        TxtInvalid.Visibility   = Selected != null && !exists ? Visibility.Visible : Visibility.Collapsed;
        TxtNoCommand.Visibility = Selected != null && !runsCommand ? Visibility.Visible : Visibility.Collapsed;
        BtnTest.IsEnabled = exists;
        TxtPreview.Text = exists
            ? PresetService.TerminalCommand(terminal, PresetService.ClaudeRunCommand(SettingsService.LoadClaudeArgs()))
            : "";
    }

    private void Apply_Click(object sender, RoutedEventArgs e)
    {
        if (Selected is not { } terminal) return;

        terminal.Name        = TxtName.Text.Trim();
        terminal.ExePath     = TxtExe.Text.Trim().Trim('"');
        terminal.FolderArgs  = TxtFolderArgs.Text.Trim();
        terminal.CommandArgs = TxtCommandArgs.Text.Trim();
        Save();
        RefreshList(selectId: terminal.Id);
    }

    private void UseAsDefault_Click(object sender, RoutedEventArgs e)
    {
        if (Selected is not { } terminal) return;

        SettingsService.SaveDefaultTerminal(terminal.Id);
        RefreshList(selectId: terminal.Id);
    }

    private void Test_Click(object sender, RoutedEventArgs e) =>
        Test(EditedTerminal(), PresetService.ClaudeRunCommand(SettingsService.LoadClaudeArgs()), owner: this);

    /// <summary>
    /// Lance <paramref name="command"/> dans <paramref name="terminal"/>, ouvert sur le dossier utilisateur.
    /// </summary>
    public static void Test(TerminalInfo terminal, string command, Window owner)
    {
        string folder = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        try
        {
            ShortcutLauncher.OpenTerminal(terminal, folder, command);
        }
        catch (Exception ex)
        {
            LogService.Error(ex, $"Test du terminal : {terminal.ExePath}");
            AppDialog.Error(Loc.F("Settings_Terminal_TestFailed", ex.Message), owner: owner);
        }
    }

    private void BrowseExe_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new OpenFileDialog { Filter = Loc.T("Settings_Terminal_PickFilter"), CheckFileExists = true };
        if (dlg.ShowDialog(this) != true) return;

        TxtExe.Text = dlg.FileName;
        if (TxtName.Text.Trim() == "" || TxtName.Text.Trim() == Loc.T("Terminals_NewTerminal"))
            TxtName.Text = Path.GetFileNameWithoutExtension(dlg.FileName);
        // Des arguments vides ne lanceraient rien d'utile : on part de ceux que DockPad connaît pour ce programme.
        if (TxtFolderArgs.Text.Trim() == "" && TxtCommandArgs.Text.Trim() == "")
        {
            var defaults = TerminalDetectionService.DefaultTemplates(TerminalDetectionService.KnownIdOf(dlg.FileName));
            TxtFolderArgs.Text  = defaults.FolderArgs;
            TxtCommandArgs.Text = defaults.CommandArgs;
        }
    }

    // ── Liste ───────────────────────────────────────────────────────────────────

    private void Redetect_Click(object sender, RoutedEventArgs e)
    {
        int added = TerminalCatalog.Merge(_config.Terminals, TerminalDetectionService.Detect());
        Save();
        RefreshList(selectId: Selected?.Id);

        AppDialog.Info(added > 0 ? Loc.F("Terminals_DetectedAdded", added) : Loc.T("Terminals_DetectedNothing"), owner: this);
    }

    private void Up_Click(object sender, RoutedEventArgs e)   => MoveSelected(-1);
    private void Down_Click(object sender, RoutedEventArgs e) => MoveSelected(+1);

    private void MoveSelected(int delta)
    {
        if (Selected is not { } terminal) return;

        TerminalCatalog.Move(_config.Terminals, terminal, delta);
        Save();
        RefreshList(selectId: terminal.Id);
    }

    private void VisibleCheck_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not System.Windows.Controls.CheckBox check || check.DataContext is not TerminalItem item) return;

        item.Entry.Hidden = check.IsChecked != true;
        Save();
        // Hors du gestionnaire : la liste recrée ses lignes, on ne détruit pas la case pendant son propre événement.
        Dispatcher.BeginInvoke(() => RefreshList(selectId: item.Entry.Id));
    }

    private void Delete_Click(object sender, RoutedEventArgs e)
    {
        if (Selected is not { } terminal) return;
        if (!AppDialog.Confirm(Loc.F("Terminals_ConfirmDelete", terminal.Name), owner: this)) return;

        _config.Terminals.Remove(terminal);
        TerminalCatalog.Reindex(_config.Terminals);
        Save();
        if (SettingsService.LoadDefaultTerminal() == terminal.Id) SettingsService.SaveDefaultTerminal("");
        RefreshList();
    }

    private void Add_Click(object sender, RoutedEventArgs e)
    {
        // Le nom par défaut est traduit à la création, puis devient une donnée : il ne suit pas la langue.
        string name = Loc.T("Terminals_NewTerminal");
        var terminal = new TerminalEntry
        {
            Id    = TerminalCatalog.NewId(name, _config.Terminals.Select(t => t.Id)),
            Name  = name,
            Order = _config.Terminals.Count == 0 ? 0 : _config.Terminals.Max(t => t.Order) + 1,
        };
        _config.Terminals.Add(terminal);
        Save();
        RefreshList(selectId: terminal.Id);
        TxtName.Focus();
        TxtName.SelectAll();
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();
}
