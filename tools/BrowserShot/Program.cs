using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using DockPad;
using DockPad.Models;

namespace BrowserShot;

/// <summary>
/// Capture les fenêtres du sélecteur de navigateur en PNG (doc + vérification du rendu
/// des profils sans lancer DockPad).
///
/// Usage : BrowserShot &lt;cible&gt; &lt;cheminPng&gt;
///   picker         popup « Ouvrir avec… », config de démonstration en mémoire
///   picker-header  idem, 1er navigateur masqué → titre de groupe non choisissable
///   config         fenêtre « Navigateurs » — lit la VRAIE config (%APPDATA%),
///                  donc à relire avant publication (noms de profils réels)
///
/// La config de démonstration ne touche pas %APPDATA% et impose autoOpenSeconds = 0
/// (sinon la capture ouvrirait vraiment un navigateur).
/// Mêmes pièges WPF que McpShot (STAThread, App sans Run, Show + Dispatcher.Run…).
/// </summary>
internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        string target = args[0];
        string outPath = Path.GetFullPath(args[1]);

        var app = new App();
        app.InitializeComponent();
        app.ShutdownMode = ShutdownMode.OnExplicitShutdown;

        Window win = target switch
        {
            "picker"        => new BrowserPickerWindow(DemoUrl, DemoConfig(hideFirst: false)) { Topmost = true },
            "picker-header" => new BrowserPickerWindow(DemoUrl, DemoConfig(hideFirst: true))  { Topmost = true },
            "config"        => new BrowserConfigDialog { WindowStartupLocation = WindowStartupLocation.CenterScreen },
            _ => throw new ArgumentException($"cible inconnue : {target} (picker | picker-header | config)"),
        };

        win.ContentRendered += (_, _) =>
        {
            // DispatcherTimer : délai et capture restent sur le thread UI (pas de
            // SynchronizationContext avec Dispatcher.Run brut, un await partirait
            // sur le thread pool et les appels WPF échoueraient).
            var timer = new System.Windows.Threading.DispatcherTimer
            { Interval = TimeSpan.FromMilliseconds(600) };
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
                    Console.WriteLine($"capturé : {outPath} ({target})");
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"ERREUR : {ex}");
                }
                finally
                {
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

    private const string DemoUrl = "https://github.com/anthropics/claude-code/pull/1234";

    /// <summary>Deux navigateurs à profils + un sans profil, noms de démonstration.</summary>
    private static BrowsersConfig DemoConfig(bool hideFirst)
    {
        const string chrome = @"C:\Program Files\Google\Chrome\Application\chrome.exe";
        var canary = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                                  @"Google\Chrome SxS\Application\chrome.exe");
        const string edge = @"C:\Program Files (x86)\Microsoft\Edge\Application\msedge.exe";

        int order = 0;
        BrowserEntry Parent(string id, string name, string icon) =>
            new() { Id = id, Name = name, ExePath = icon.Split(',')[0], IconPath = icon, Order = order++ };
        BrowserEntry Profile(string id, string parentId, string name, string dir, string icon) =>
            new()
            {
                Id = id, ParentId = parentId, Name = name, ProfileDirectory = dir, DetectedName = name,
                ExePath = icon.Split(',')[0], IconPath = icon, Order = order++,
            };

        var config = new BrowsersConfig
        {
            AutoOpenSeconds = 0, // jamais de lancement réel pendant une capture
            Browsers =
            [
                Parent("chrome00", "Google Chrome", chrome),
                Profile("chromep1", "chrome00", "Boulot", "Default",   chrome),
                Profile("chromep2", "chrome00", "Perso",  "Profile 1", chrome),
                // Canary : icône jaune = index 4 de l'exe (comme la détection registre)
                Parent("canary00", "Google Chrome Canary", canary + ",4"),
                Profile("canaryp1", "canary00", "Démo",  "Default",   canary + ",4"),
                Profile("canaryp2", "canary00", "Tests", "Profile 1", canary + ",4"),
                Parent("edge0000", "Microsoft Edge", edge),
            ],
        };

        if (hideFirst) config.Browsers[0].Hidden = true;
        return config;
    }
}
