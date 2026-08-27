using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using DockPad;
using DockPad.Models;
using DockPad.Services;

namespace BrowserShot;

/// <summary>
/// Capture les fenêtres du sélecteur de navigateur en PNG (doc + vérification d'un
/// rendu sans lancer DockPad).
///
/// Usage : BrowserShot &lt;cible&gt; &lt;cheminPng&gt; [tabIndex]
///   picker         popup « Ouvrir avec… » : 2 navigateurs à profils + 1 sans profil
///   picker-header  idem, 1er navigateur masqué → titre de groupe non choisissable
///   config         fenêtre « Navigateurs » (tabIndex 0 = Navigateurs, 1 = Règles)
///
/// Les données affichées viennent d'un **profil de fixture** dans %TEMP% : la variable
/// DOCKPAD_PROFILE_DIR est posée avant tout accès aux services (voir AppPaths), donc
/// le profil réel de l'utilisateur n'est ni lu ni écrit. autoOpenSeconds = 0, sinon la
/// capture ouvrirait vraiment un navigateur.
///
/// Mêmes pièges WPF que McpShot (STAThread, App sans Run, Show + Dispatcher.Run…).
/// </summary>
internal static class Program
{
    /// <summary>Basculer le theme APRES construction, comme le fait l'utilisateur.</summary>
    private static bool _switchAfterBuild;

    [STAThread]
    private static void Main(string[] args)
    {
        string target = args[0];
        string outPath = Path.GetFullPath(args[1]);
        int tab = args.Length > 2 && int.TryParse(args[2], out int t) ? t : 0;

        // Doit précéder toute utilisation des services : AppPaths ne lit la variable
        // qu'une seule fois, à la première résolution de ProfileRoot.
        var fixtureDir = Path.Combine(Path.GetTempPath(), "dockpad-browsershot");
        Directory.CreateDirectory(fixtureDir);
        Environment.SetEnvironmentVariable(AppPaths.OverrideVariable, fixtureDir);

        var app = new App();
        app.InitializeComponent();
        app.ShutdownMode = ShutdownMode.OnExplicitShutdown;

        // Theme de capture : une variable d'environnement plutot qu'un argument, pour ne pas
        // deplacer les arguments des cibles existantes — comme DOCKPAD_SHOT_LANG.
        //
        // « dark-switch » bascule APRES construction, comme le fait l'utilisateur depuis les
        // Options. C'est un autre chemin que « dark », et le seul qui revele une couleur deja
        // resolue.
        var themeEnv = Environment.GetEnvironmentVariable("DOCKPAD_SHOT_THEME") ?? "";
        _switchAfterBuild = string.Equals(themeEnv, "dark-switch", StringComparison.OrdinalIgnoreCase);
        if (!_switchAfterBuild)
            DockPad.Services.ThemeService.Apply(
                string.Equals(themeEnv, "dark", StringComparison.OrdinalIgnoreCase));

        // Langue de la capture : variable d'environnement plutot qu'un argument, pour ne pas
        // deplacer les arguments existants des cibles.
        var shotLang = Environment.GetEnvironmentVariable("DOCKPAD_SHOT_LANG");
        if (!string.IsNullOrWhiteSpace(shotLang))
        {
            // Loc.Parse et non GetCultureInfo : une etiquette inconnue vaut « automatique » au lieu
            // de lever une CultureNotFoundException au milieu d'une capture.
            DockPad.Services.Localization.Loc.SetCulture(
                DockPad.Services.Localization.Loc.Parse(shotLang));
            // Sans ca, Window.Language reste au defaut et les StringFormat de liaison rendent en
            // en-US : OnStartup, qui s'en charge normalement, n'est jamais appele ici.
            DockPad.App.ApplyWpfLanguage();
        }


        Window win;
        switch (target)
        {
            case "picker":
            case "picker-header":
                win = new BrowserPickerWindow(DemoUrl,
                    DemoConfig(hideFirst: target == "picker-header", withCanary: true)) { Topmost = true };
                break;

            case "config":
                // La fenêtre lit la config du profil : on écrit la fixture avant de l'ouvrir
                // (moins d'entrées que pour la popup, pour tenir sans barre de défilement).
                BrowserConfigService.Save(DemoConfig(hideFirst: false, withCanary: false, autoOpen: 3));
                win = new BrowserConfigDialog
                {
                    WindowStartupLocation = WindowStartupLocation.CenterScreen,
                    // Fenêtre redimensionnable : un peu plus haute que par défaut pour que la
                    // liste et le panneau d'édition tiennent ensemble sur la capture.
                    Height = 840,
                };
                SelectTab(win, tab);
                // Sélection AVANT Show, comme l'onglet : le panneau d'édition montre alors
                // le chemin d'exe grisé et la ligne d'information du profil.
                if (tab == 0) SelectRow(win, 1);
                break;

            default:
                throw new ArgumentException($"cible inconnue : {target} (picker | picker-header | config)");
        }

        win.ContentRendered += (_, _) =>
        {
            // DispatcherTimer : délai et capture restent sur le thread UI (pas de
            // SynchronizationContext avec Dispatcher.Run brut, un await partirait sur
            // le thread pool et les appels WPF échoueraient).
            var timer = new System.Windows.Threading.DispatcherTimer
            { Interval = TimeSpan.FromMilliseconds(600) };
            timer.Tick += (_, _) =>
            {
                timer.Stop();
                try
                {
                    win.UpdateLayout();

                    if (_switchAfterBuild)
                    {
                        DockPad.Services.ThemeService.Apply(dark: true);
                        win.UpdateLayout();
                    }

                    // GetDpi plutôt que PresentationSource.FromVisual (nul dans ce contexte hébergé)
                    double sx = VisualTreeHelper.GetDpi(win).DpiScaleX;
                    var rtb = new RenderTargetBitmap(
                        (int)Math.Ceiling(win.ActualWidth * sx), (int)Math.Ceiling(win.ActualHeight * sx),
                        96.0 * sx, 96.0 * sx, PixelFormats.Pbgra32);
                    rtb.Render(win);
                    var enc = new PngBitmapEncoder();
                    enc.Frames.Add(BitmapFrame.Create(rtb));
                    using (var fs = File.Create(outPath)) enc.Save(fs);
                    Console.WriteLine($"capturé : {outPath} ({target}{(target == "config" ? $", onglet {tab}" : "")})");
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

    /// <summary>Onglet sélectionné AVANT affichage (une bascule post-rendu ne se répercute pas).</summary>
    private static void SelectTab(Window win, int tab)
    {
        var tabControl = LogicalTreeHelper.GetChildren(win).OfType<Grid>().First()
            .Children.OfType<TabControl>().First();
        tabControl.SelectedIndex = tab;
    }

    /// <summary>Sélectionne une ligne de la liste des navigateurs (index dans l'ordre affiché).</summary>
    private static void SelectRow(Window win, int index)
    {
        if (win.FindName("LstBrowsers") is not ListBox list)
        {
            Console.WriteLine("AVERTISSEMENT : LstBrowsers introuvable, aucune ligne sélectionnée.");
            return;
        }
        if (index < list.Items.Count) list.SelectedIndex = index;
    }

    private const string DemoUrl = "https://github.com/anthropics/claude-code/pull/1234";

    /// <summary>Navigateurs de démonstration : noms neutres, aucune donnée personnelle.</summary>
    private static BrowsersConfig DemoConfig(bool hideFirst, bool withCanary, int autoOpen = 0)
    {
        const string chrome = @"C:\Program Files\Google\Chrome\Application\chrome.exe";
        const string edge = @"C:\Program Files (x86)\Microsoft\Edge\Application\msedge.exe";
        // Canary : icône jaune = index 4 de l'exe (comme la valeur DefaultIcon du registre)
        var canary = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                                  @"Google\Chrome SxS\Application\chrome.exe") + ",4";

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
            // 0 pour la popup : un décompte ouvrirait vraiment un navigateur pendant la capture.
            AutoOpenSeconds = autoOpen,
            Browsers =
            [
                Parent("chrome00", "Google Chrome", chrome),
                Profile("chromep1", "chrome00", "Boulot", "Default",   chrome),
                Profile("chromep2", "chrome00", "Perso",  "Profile 1", chrome),
            ],
            Rules =
            [
                new BrowserRule { Host = "github.com",       BrowserId = "chromep1" },
                new BrowserRule { Host = "dev.azure.com",    BrowserId = "chromep1" },
                new BrowserRule { Host = "localhost:44351",  BrowserId = "chromep2" },
                new BrowserRule { Host = "news.ycombinator.com", BrowserId = "edge0000" },
            ],
        };

        if (withCanary)
        {
            config.Browsers.Add(Parent("canary00", "Google Chrome Canary", canary));
            config.Browsers.Add(Profile("canaryp1", "canary00", "Démo",  "Default",   canary));
            config.Browsers.Add(Profile("canaryp2", "canary00", "Tests", "Profile 1", canary));
        }

        config.Browsers.Add(Parent("edge0000", "Microsoft Edge", edge));

        if (hideFirst) config.Browsers[0].Hidden = true;
        return config;
    }
}
