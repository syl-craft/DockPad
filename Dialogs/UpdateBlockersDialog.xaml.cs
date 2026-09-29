using System.Windows;
using DockPad.Services;
using DockPad.Services.Updates;
using System.Threading.Tasks;

namespace DockPad;

public partial class UpdateBlockersDialog : Window
{
    private readonly string _directory;
    private bool _working;
    protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
    {
        if (_working) e.Cancel = true;
        base.OnClosing(e);
    }
    public UpdateBlockersDialog(string directory, IReadOnlyList<UpdateBlocker> blockers)
    {
        InitializeComponent(); _directory = directory; Processes.ItemsSource = blockers;
    }
    private async Task RefreshAsync()
    {
        var blockers = await Task.Run(() => UpdateBlockers.Find(_directory));
        if (blockers.Count == 0) { _working = false; DialogResult = true; return; }
        Processes.ItemsSource = blockers;
    }
    private async void Retry_Click(object sender, RoutedEventArgs e) => await RunAsync(RefreshAsync);
    private async void CloseSelected_Click(object sender, RoutedEventArgs e)
    {
        var selected = Processes.Items.Cast<UpdateBlocker>().Where(p => p.Selected).ToArray();
        if (selected.Length == 0) return;
        await RunAsync(async () =>
        {
            var remaining = new List<UpdateBlocker>();
            foreach (var item in selected)
                if (!await UpdateBlockers.CloseAsync(item, force: false)) remaining.Add(item);
            if (remaining.Count > 0 && AppDialog.Confirm(Loc.F("Update_ForcePrompt",
                    string.Join(Environment.NewLine, remaining.Select(p => p.Label))), Loc.T("Update_BlockedTitle"), this))
                foreach (var item in remaining) await UpdateBlockers.CloseAsync(item, force: true);
            await RefreshAsync();
        });
    }
    private async Task RunAsync(Func<Task> work)
    {
        if (_working) return;
        _working = true;
        RetryButton.IsEnabled = CloseButton.IsEnabled = false; ErrorText.Text = "";
        try { await work(); }
        catch (Exception ex) { LogService.Warn(ex, "Cannot resolve update locks"); ErrorText.Text = Loc.T("Update_LockError"); }
        finally { _working = false; RetryButton.IsEnabled = CloseButton.IsEnabled = true; }
    }
}
