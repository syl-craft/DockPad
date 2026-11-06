using System.ComponentModel;
using System.Windows;
using DockPad.Services.Updates;
using System.Threading.Tasks;

namespace DockPad;

public partial class UpdateBlockersDialog : Window
{
    private readonly Func<Task<IReadOnlyList<UpdateBlocker>>> _refresh;
    private readonly Func<UpdateBlocker, bool, Task<bool>> _close;
    private readonly IUpdateLog _log;
    private readonly bool _forcesShutdown;
    private bool _working;
    private bool _incomplete;
    private readonly HashSet<(int Pid, long Started)> _remaining = [];
    private IReadOnlyList<UpdateBlocker> _blockers = [];

    // Les captures injectent aussi les opérations : aucun clic de fixture ne consulte les vrais processus.
    public UpdateBlockersDialog(string directory, IReadOnlyList<UpdateBlocker> blockers, IUpdateDialogHost host, bool forcesShutdown,
        Func<Task<IReadOnlyList<UpdateBlocker>>>? refresh = null,
        Func<UpdateBlocker, bool, Task<bool>>? close = null, bool incomplete = false)
    {
        InitializeComponent();
        _log = host.Log;
        _forcesShutdown = forcesShutdown;
        _refresh = refresh ?? (() => Task.Run(() => host.Finder.Find(directory, host.Classifier)));
        _close = close ?? host.Finder.CloseAsync;
        if (incomplete)
            foreach (var blocker in blockers.Where(b => b.Selected)) _remaining.Add((blocker.Pid, blocker.Started));
        ShowBlockers(blockers);
        UpdateTexts.LanguageChanged += LanguageChanged;
        Closed += (_, _) =>
        {
            UpdateTexts.LanguageChanged -= LanguageChanged;
            foreach (var blocker in _blockers) blocker.PropertyChanged -= Blocker_PropertyChanged;
        };
    }
    protected override void OnClosing(CancelEventArgs e)
    {
        if (_working) e.Cancel = true;
        base.OnClosing(e);
    }
    private void LanguageChanged(object? sender, EventArgs e) => ShowBlockers(_blockers);
    private void ShowBlockers(IReadOnlyList<UpdateBlocker> blockers)
    {
        foreach (var blocker in _blockers) blocker.PropertyChanged -= Blocker_PropertyChanged;
        _blockers = blockers;
        foreach (var blocker in _blockers) blocker.PropertyChanged += Blocker_PropertyChanged;
        _remaining.IntersectWith(blockers.Select(b => (b.Pid, b.Started)));
        _incomplete = _remaining.Count > 0;
        Processes.ItemsSource = UpdateBlockerGroups.Group(blockers.Where(b => !_remaining.Contains((b.Pid, b.Started))).ToArray());
        RemainingProcesses.ItemsSource = blockers.Where(b => _remaining.Contains((b.Pid, b.Started))).ToArray();
        UpdateLabels();
    }
    private void Blocker_PropertyChanged(object? sender, PropertyChangedEventArgs e) => UpdateLabels();
    private void UpdateLabels()
    {
        StateLabel.Text = UpdateTexts.T(_incomplete ? "Update_IncompleteState" : "Update_StatePreparing").ToUpper(UpdateTexts.Formatting);
        Heading.Text = _incomplete ? UpdateTexts.F("Update_IncompleteTitle", _remaining.Count) : UpdateTexts.T("Update_BlockedTitle");
        HelpText.Text = _forcesShutdown
            ? UpdateTexts.T(_incomplete ? "Update_IncompleteHelpForced" : "Update_BlockedHelpForced")
            : UpdateTexts.T(_incomplete ? "Update_IncompleteHelp" : "Update_BlockedHelp");
        ForceWarning.Visibility = _incomplete ? Visibility.Visible : Visibility.Collapsed;
        RemainingCard.Visibility = _incomplete ? Visibility.Visible : Visibility.Collapsed;
        // Les programmes encore ouverts sans qu'on ait tenté de les fermer passent sous les restants, avec leur titre.
        OtherHeading.Visibility = _incomplete && Processes.HasItems ? Visibility.Visible : Visibility.Collapsed;
        Processes.Visibility = Visibility.Visible;
        CloseButton.Content = _incomplete ? UpdateTexts.T("Update_ForceInstall") : UpdateTexts.F("Update_CloseCount", _blockers.Where(b => b.Selected).Count());
        CloseButton.SetResourceReference(StyleProperty, _incomplete ? "DangerButton" : "PrimaryButton");
        CloseButton.IsEnabled = !_working && (_incomplete || _blockers.Where(b => b.Selected).Any());
        StateLabel.SetResourceReference(TextBlock.ForegroundProperty, _incomplete ? "Brush.Danger" : "Brush.TextLabel");
    }
    private async Task RefreshAsync()
    {
        var blockers = await _refresh();
        if (blockers.Count == 0) { ShowBlockers([]); _working = false; DialogResult = true; return; }
        // Une actualisation préserve le choix pour une même identité, pas pour un PID réutilisé.
        var choices = _blockers.ToDictionary(b => (b.Pid, b.Started), b => b.Selected);
        foreach (var blocker in blockers)
            if (choices.TryGetValue((blocker.Pid, blocker.Started), out var selected)) blocker.Selected = selected;
        ShowBlockers(blockers);
    }
    private async void Retry_Click(object sender, RoutedEventArgs e) => await RunAsync(RefreshAsync);
    private async void CloseSelected_Click(object sender, RoutedEventArgs e)
    {
        var selected = _blockers.Where(b => _incomplete ? _remaining.Contains((b.Pid, b.Started)) : b.Selected).ToArray();
        if (selected.Length == 0) return;
        await RunAsync(async () =>
        {
            // Le contrôle d'identité est refait par CloseAsync pour CHAQUE arrêt, y compris forcé.
            await Task.WhenAll(selected.Select(item => _close(item, _incomplete)));
            foreach (var blocker in selected) _remaining.Add((blocker.Pid, blocker.Started));
            await RefreshAsync();
        });
    }
    private async Task RunAsync(Func<Task> work)
    {
        if (_working) return;
        _working = true;
        RetryButton.IsEnabled = CloseButton.IsEnabled = PostponeButton.IsEnabled = Processes.IsEnabled = false;
        ErrorText.Text = "";
        try { await work(); }
        catch (Exception ex) { _log.Warn(ex, "Cannot resolve update locks"); ErrorText.Text = UpdateTexts.T("Update_LockError"); }
        finally
        {
            _working = false;
            RetryButton.IsEnabled = PostponeButton.IsEnabled = Processes.IsEnabled = true;
            UpdateLabels();
        }
    }
}
