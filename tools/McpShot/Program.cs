using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using DockPad;
using DockPad.Services;

namespace McpShot;

/// <summary>
/// Capture les onglets de McpConfigDialog en PNG pour la doc (docs/screenshots/).
/// Usage : McpShot &lt;tabIndex&gt; &lt;cheminPng&gt;  — ex. McpShot 0 mcp-options.png
/// Hors process DockPad : instancie la vraie fenêtre avec les ressources App.xaml,
/// des entrées de journal de démonstration, et rend via RenderTargetBitmap.
/// </summary>
internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        int tab = int.Parse(args[0]);
        string outPath = Path.GetFullPath(args[1]);

        // Application.Current + ressources App.xaml, sans Run() (OnStartup — mutex,
        // systray, fenêtres — ne doit jamais s'exécuter ici).
        var app = new App();
        app.InitializeComponent();
        // L'outil EST l'assembly d'entree, mais il montre les fenetres de DockPad : sans
        // cette pose, le pied de fenetre porterait la version de l'outil (v1.0.0).
        DockPad.Services.AppInfo.Initialize(typeof(App).Assembly);
        app.ShutdownMode = ShutdownMode.OnExplicitShutdown;

        // Journal de démonstration (insertion en tête : la plus récente en premier).
        McpLogService.Add("dockpad_grid_get", "", McpLogStatus.Success);
        McpLogService.Add("dockpad_page_add", "{\"iconPath\":null}", McpLogStatus.Success);
        McpLogService.Add("dockpad_shortcut_add",
            "{\"items\":[{\"name\":\"VS Code\",\"command\":\"code\"},{\"name\":\"Terminal\",\"command\":\"wt.exe\"}]}",
            McpLogStatus.Success);
        McpLogService.Add("dockpad_shortcut_delete", "{\"page\":0,\"row\":1,\"col\":2}",
            McpLogStatus.Refused, "La suppression via MCP est désactivée (fenêtre Serveur MCP → Options).");

        var win = new McpConfigDialog
        {
            WindowStartupLocation = WindowStartupLocation.CenterScreen,
            Topmost = true,
        };

        // Ce process n'est pas DockPad.exe : remettre le chemin déployé dans les commandes affichées.
        ((TextBox)win.FindName("TxtClaudeCodeCmd")).Text =
            "claude mcp add dockpad -s user -- \"C:\\DockPad\\DockPad.exe\" --mcp";
        ((TextBox)win.FindName("TxtClaudeDesktopCfg")).Text =
            "\"dockpad\": {\n  \"command\": \"C:\\\\DockPad\\\\DockPad.exe\",\n  \"args\": [\"--mcp\"]\n}";

        // Sélection de l'onglet AVANT affichage (une bascule après rendu ne se répercute
        // pas de façon fiable hors vraie session interactive).
        var tabControl = LogicalTreeHelper.GetChildren(win).OfType<Grid>().First()
            .Children.OfType<TabControl>().First();
        tabControl.SelectedIndex = tab;

        win.ContentRendered += (_, _) =>
        {
            // DispatcherTimer : délai et capture restent sur le thread UI — un await
            // reprendrait sur le thread pool (pas de SynchronizationContext avec
            // Dispatcher.Run brut) et les appels WPF échoueraient.
            var timer = new System.Windows.Threading.DispatcherTimer
            { Interval = TimeSpan.FromMilliseconds(700) };
            timer.Tick += (_, _) =>
            {
                timer.Stop();
                try
                {
                    win.UpdateLayout();
                    // GetDpi plutôt que PresentationSource.FromVisual (nul dans ce contexte hébergé)
                    double sx = VisualTreeHelper.GetDpi(win).DpiScaleX;
                    var rtb = new RenderTargetBitmap(
                        (int)Math.Ceiling(win.ActualWidth * sx), (int)Math.Ceiling(win.ActualHeight * sx),
                        96.0 * sx, 96.0 * sx, PixelFormats.Pbgra32);
                    rtb.Render(win);
                    var enc = new PngBitmapEncoder();
                    enc.Frames.Add(BitmapFrame.Create(rtb));
                    using (var fs = File.Create(outPath)) enc.Save(fs);
                    Console.WriteLine($"capturé : {outPath} (onglet {tab})");
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"ERREUR : {ex}");
                }
                finally
                {
                    win.Close();
                    win.Dispatcher.InvokeShutdown();
                }
            };
            timer.Start();
        };

        // Show + Dispatcher.Run : ShowDialog retourne immédiatement dans ce contexte
        // (Application jamais Run) — la boucle de messages doit être pompée à la main.
        win.Show();
        System.Windows.Threading.Dispatcher.Run();
    }
}
