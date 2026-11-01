using System.ComponentModel;
using System.IO;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Documents;
using System.Windows.Threading;
using DockPad.Services;
using DockPad.Services.Updates;

namespace DockPad;

public partial class UpdatesDialog : Window
{
    private readonly UpdateService _updates;
    private bool _preparing;
    private UpdateNotes? _renderedNotes;
    private readonly DispatcherTimer _clock = new() { Interval = TimeSpan.FromMinutes(1) };
    public UpdatesDialog() : this(UpdateService.Current) { }
    public UpdatesDialog(UpdateService updates)
    {
        InitializeComponent(); _updates = updates; DataContext = updates;
        Automatic.IsChecked = AppSettingsService.Current.CheckForUpdates;
        _updates.PropertyChanged += Updates_Changed;
        _clock.Tick += Clock_Tick;
        _clock.Start();
        Closed += (_, _) =>
        {
            _updates.PropertyChanged -= Updates_Changed;
            _clock.Stop();
            _clock.Tick -= Clock_Tick;
        };
        RefreshDisplay();
    }
    private void Automatic_Click(object sender, RoutedEventArgs e) =>
        AppSettingsService.Update(s => s.CheckForUpdates = Automatic.IsChecked == true);
    private void Clock_Tick(object? sender, EventArgs e) => LastChecked.GetBindingExpression(TextBlock.TextProperty)?.UpdateTarget();
    private void Updates_Changed(object? sender, PropertyChangedEventArgs e)
    {
        if (!Dispatcher.CheckAccess()) { Dispatcher.BeginInvoke(RefreshDisplay); return; }
        RefreshDisplay();
    }
    private void RefreshDisplay()
    {
        InstallButton.SetResourceReference(StyleProperty, _updates.PrimaryAccent ? "PrimaryButton" : "SecondaryButton");
        RenderNotes();
    }
    private void RenderNotes()
    {
        if (ReferenceEquals(_renderedNotes, _updates.ParsedNotes)) return;
        _renderedNotes = _updates.ParsedNotes;
        NotesBlocks.Children.Clear();
        foreach (var block in _renderedNotes.Blocks)
        {
            var text = new TextBlock { TextWrapping = TextWrapping.Wrap, FontSize = 13,
                Margin = new Thickness(block.Kind == UpdateNoteKind.Bullet ? 18 + block.Indent * 6 : 0,
                    block.Kind is UpdateNoteKind.Heading or UpdateNoteKind.Subheading ? 10 : 0, 8, 6) };
            if (block.Kind is UpdateNoteKind.Heading or UpdateNoteKind.Subheading) text.FontWeight = FontWeights.SemiBold;
            if (block.Kind == UpdateNoteKind.Bullet) text.Inlines.Add(new Run("•  "));
            foreach (var inline in block.Inlines)
            {
                var run = new Run(inline.Text);
                if (inline.Kind == UpdateInlineKind.Bold) run.FontWeight = FontWeights.SemiBold;
                if (inline.Kind == UpdateInlineKind.Italic) run.FontStyle = FontStyles.Italic;
                if (inline.Kind == UpdateInlineKind.Code)
                {
                    run.FontFamily = new System.Windows.Media.FontFamily("Consolas");
                    run.SetResourceReference(TextElement.BackgroundProperty, "Brush.SurfaceMuted");
                }
                text.Inlines.Add(run);
            }
            NotesBlocks.Children.Add(text);
        }
    }
    private async void Secondary_Click(object sender, RoutedEventArgs e) => await ExecuteAsync(_updates.Presentation.Secondary, sender, e);
    private async void Primary_Click(object sender, RoutedEventArgs e) => await ExecuteAsync(_updates.Presentation.Primary, sender, e);
    private async Task ExecuteAsync(UpdateAction action, object sender, RoutedEventArgs e)
    {
        switch (action)
        {
            case UpdateAction.Check: await _updates.CheckAsync(); break;
            case UpdateAction.Close: Close(); break;
            case UpdateAction.Stop: _updates.CancelDownload(); break;
            case UpdateAction.Install: Install_Click(sender, e); break;
            case UpdateAction.DownloadLatest: DownloadLatest_Click(sender, e); break;
        }
    }
    private void GitHub_Click(object sender, RoutedEventArgs e)
    {
        var version = (_updates.Release?.Version ?? _updates.CurrentVersion).TrimStart('v');
        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(
            "https://github.com/syl-craft/DockPad/releases/tag/v" + Uri.EscapeDataString(version)) { UseShellExecute = true });
    }
    private void DownloadLatest_Click(object sender, RoutedEventArgs e) =>
        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("https://github.com/syl-craft/DockPad/releases/latest") { UseShellExecute = true });
    protected override void OnClosing(CancelEventArgs e)
    {
        if (_preparing && !App.IsExiting) e.Cancel = true;
        if (!e.Cancel) _updates.CancelDownload();
        base.OnClosing(e);
    }
    private async void Install_Click(object sender, RoutedEventArgs e)
    {
        if (_preparing || !await _updates.DownloadAsync()) return;
        _preparing = true; InstallButton.IsEnabled = false;
        try
        {
            _updates.BeginPreparation();
            if (AppInstallation.IsWithin(AppPaths.ProfileRoot, AppInstallation.Root ?? _updates.ContentDirectory))
                throw new IOException(Loc.T("Update_ProfileInside"));
            if (Application.Current is not App app || !await app.PrepareForUpdateAsync(this)) return;
            var blockers = await Task.Run(() => UpdateBlockers.Find(_updates.ContentDirectory));
            if (blockers.Count > 0 && new UpdateBlockersDialog(_updates.ContentDirectory, blockers) { Owner = this }.ShowDialog() != true) return;
            // A final check improves feedback. The native updater independently refuses any race.
            if ((await Task.Run(() => UpdateBlockers.Find(_updates.ContentDirectory, enrich: false))).Count > 0)
                throw new IOException(Loc.T("Update_LockError"));
            if (!await app.PrepareForUpdateAsync(this)) return;
            AppInstallation.BeginUpdate();
            try { app.BeginUpdateHandoff(); _updates.Apply(); }
            catch { AppInstallation.EndUpdate(); app.CancelUpdateHandoff(); throw; }
            // Drain accepted relay requests: after the marker they go to the stable launcher.
            _ = Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.ContextIdle, new Action(App.Exit));
        }
        catch (Exception ex) { LogService.Warn(ex, "Cannot apply update"); AppDialog.Error(ex.Message, Loc.T("Update_Title"), this); }
        finally
        {
            // Sans effet une fois l'outil de mise à jour lancé : l'état est alors Installing.
            _updates.CancelPreparation();
            _preparing = false; InstallButton.SetBinding(IsEnabledProperty, new System.Windows.Data.Binding(nameof(UpdateService.PrimaryEnabled)));
        }
    }
}
