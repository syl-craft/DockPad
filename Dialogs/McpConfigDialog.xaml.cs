using System.Collections.Specialized;
using System.Windows;
using System.Windows.Threading;
using DockPad.Models;
using DockPad.Services;

namespace DockPad;

public partial class McpConfigDialog : Window
{
    private McpConfig _config = new();
    private bool _loading = true;
    private readonly NotifyCollectionChangedEventHandler _logChangedHandler;

    public McpConfigDialog()
    {
        InitializeComponent();
        _config = McpConfigService.Load();
        ChkEnabled.IsChecked = _config.Enabled;
        ChkAllowDelete.IsChecked = _config.AllowDelete;

        string exe = Environment.ProcessPath ?? "DockPad.exe";
        UpdateClaudeCodeCmd();
        TxtClaudeDesktopCfg.Text =
            "\"dockpad\": {\n" +
            $"  \"command\": \"{exe.Replace("\\", "\\\\")}\",\n" +
            "  \"args\": [\"--mcp\"]\n" +
            "}";

        TxtVersion.Text = AppInfo.VersionText;
        LstLog.ItemsSource = McpLogService.Entries;
        _logChangedHandler = (_, _) => UpdateLogCount();
        McpLogService.Entries.CollectionChanged += _logChangedHandler;
        Closed += (_, _) => McpLogService.Entries.CollectionChanged -= _logChangedHandler;
        UpdateLogCount();
        _loading = false;
    }

    /// <summary>Commande d'ajout, portée utilisateur (-s user) ou locale selon la case.</summary>
    private void UpdateClaudeCodeCmd()
    {
        string exe = Environment.ProcessPath ?? "DockPad.exe";
        string scope = ChkUserScope.IsChecked == true ? "-s user " : "";
        TxtClaudeCodeCmd.Text = $"claude mcp add dockpad {scope}-- \"{exe}\" --mcp";
    }

    private void Scope_Changed(object sender, RoutedEventArgs e)
    {
        if (_loading) return;
        UpdateClaudeCodeCmd();
    }

    private void Option_Changed(object sender, RoutedEventArgs e)
    {
        if (_loading) return;
        _config.Enabled = ChkEnabled.IsChecked == true;
        _config.AllowDelete = ChkAllowDelete.IsChecked == true;
        McpConfigService.Save(_config); // prise en compte immédiate (le dispatcher relit à chaque requête)
    }

    private void UpdateLogCount() =>
        TxtLogCount.Text = $"{McpLogService.Entries.Count} action(s) MCP cette session";

    private void ClearLog_Click(object sender, RoutedEventArgs e) => McpLogService.Clear();

    private void Close_Click(object sender, RoutedEventArgs e) => Close();

    private void CopyCmd_Click(object sender, RoutedEventArgs e) =>
        CopyWithFeedback(TxtClaudeCodeCmd.Text, BtnCopyCmd);

    private void CopyCfg_Click(object sender, RoutedEventArgs e) =>
        CopyWithFeedback(TxtClaudeDesktopCfg.Text, BtnCopyCfg);

    private void CopyRemove_Click(object sender, RoutedEventArgs e) =>
        CopyWithFeedback(TxtClaudeCodeRemove.Text, BtnCopyRemove);

    private static void CopyWithFeedback(string text, System.Windows.Controls.Button btn)
    {
        try { Clipboard.SetText(text); } catch { return; }
        var original = btn.Content;
        btn.Content = "Copié ✓";
        var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1.5) };
        timer.Tick += (_, _) => { btn.Content = original; timer.Stop(); };
        timer.Start();
    }
}
