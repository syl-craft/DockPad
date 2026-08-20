using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using DockPad;
using DockPad.Models;
using DockPad.Services;
using DockPad.Services.Usage;
using DockPad.Views;

namespace UsageShot;

/// <summary>
/// Capture le bandeau Usage IA et la fenêtre de configuration en PNG, pour la documentation et les
/// notes de version — et pour vérifier un rendu sans lancer DockPad.
/// </summary>
/// <remarks>
/// <para>
/// <b>Aucune donnée personnelle.</b> Le profil réel n'est ni lu ni écrit : l'outil pose
/// <c>DOCKPAD_PROFILE_DIR</c> sur un dossier de fixture, et n'enregistre que des
/// <see cref="DemoUsageProvider"/>. <see cref="ClaudeUsageProvider"/> est délibérément absent — les
/// vrais chiffres de consommation sont des données personnelles, et une capture doit être
/// reproductible.
/// </para>
/// <para>
/// Mêmes pièges WPF que McpShot et BrowserShot : STAThread, App sans Run, Show + Dispatcher.Run,
/// DPI via VisualTreeHelper, un processus par fenêtre.
/// </para>
/// </remarks>
internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        if (args.Length < 2)
        {
            Console.WriteLine("usage : UsageShot <panel|window> <cheminPng>");
            return;
        }

        string target = args[0];
        string outPath = Path.GetFullPath(args[1]);

        // Doit précéder toute utilisation des services : AppPaths ne lit la variable qu'une seule
        // fois, à la première résolution de ProfileRoot.
        var fixtureDir = Path.Combine(Path.GetTempPath(), "dockpad-usageshot");
        Directory.CreateDirectory(fixtureDir);
        Environment.SetEnvironmentVariable(AppPaths.OverrideVariable, fixtureDir);

        UsageConfigService.Save(FixtureConfig());

        var app = new App();
        app.InitializeComponent();
        app.ShutdownMode = ShutdownMode.OnExplicitShutdown;

        // Le bandeau seul est rendu hors écran : une fenêtre à SizeToContent mesure avant que les
        // données arrivent et n'en reprend pas la hauteur, ce qui rognait la capture. Measure et
        // Arrange explicites donnent des dimensions déterministes.
        if (target == "panel")
        {
            CapturePanel(outPath);
            return;
        }

        Window win;
        switch (target)
        {
            case "window":
                var quick = new QuickAccessWindow { WindowStartupLocation = WindowStartupLocation.CenterScreen };
                // Le bandeau se construit avec le registre de production, qui lirait le vrai profil
                // Claude : on le remplace par la fixture avant tout affichage.
                quick.UsageBannerPanel.ViewModel = FixtureViewModel();
                // La fenêtre a déjà démarré son bandeau sur l'ancien ViewModel via
                // IsVisibleChanged : il faut relancer celui qu'on vient de substituer.
                quick.Loaded += (_, _) => quick.UsageBannerPanel.Start();
                win = quick;
                break;

            default:
                throw new ArgumentException($"cible inconnue : {target} (panel | window)");
        }

        win.ContentRendered += (_, _) => CaptureThenExit(win, outPath, target);

        // Show + Dispatcher.Run : ShowDialog retourne immédiatement ici (Application jamais Run).
        win.Show();
        System.Windows.Threading.Dispatcher.Run();
    }

    /// <summary>Rend le bandeau seul, sur le fond de la grille, sans passer par une fenêtre.</summary>
    private static void CapturePanel(string outPath)
    {
        const double width = 948;   // 900 de bandeau + 24 de marge de chaque côté

        var panel = new UsagePanel { ViewModel = FixtureViewModel() };
        var frame = new Border
        {
            Padding = new Thickness(24),
            Background = new SolidColorBrush(Color.FromRgb(0xF3, 0xF3, 0xF3)),
            Child = panel,
        };

        // Les données d'abord : sans elles le bandeau est Collapsed et la mesure donne zéro.
        panel.Start();

        frame.Measure(new Size(width, double.PositiveInfinity));
        frame.Arrange(new Rect(0, 0, width, frame.DesiredSize.Height));
        frame.UpdateLayout();

        Save(frame, frame.ActualWidth, frame.ActualHeight, outPath, "panel");
    }

    /// <summary>
    /// ViewModel alimenté par des fournisseurs de démonstration seulement. C'est la raison d'être de
    /// la liste injectable de <see cref="UsageService"/> : le registre de production reste intact.
    /// </summary>
    private static UsageViewModel FixtureViewModel()
    {
        var service = new UsageService(FixtureProviders());
        return new UsageViewModel(service, UsageConfigService.Load);
    }

    /// <summary>
    /// Quatre fournisseurs plausibles. Copilot ne rapporte pas de coût (forfait) et n'a pas de
    /// quota hebdomadaire connu : la capture montre ainsi les deux cas dégradés du bandeau.
    /// </summary>
    private static List<IUsageProvider> FixtureProviders() =>
    [
        new DemoUsageProvider("claude", "Claude", "✳", "#D97757",
            new DemoUsageProvider.DemoValues("claude-opus-5", 12_400, 86_000, 1_200_000, 47, "$3.80",
                62, TimeSpan.FromHours(2) + TimeSpan.FromMinutes(40), 44, TimeSpan.FromDays(4))),

        new DemoUsageProvider("codex", "Codex", "C", "#10A37F",
            new DemoUsageProvider.DemoValues("gpt-5-codex", 8_100, 54_000, 760_000, 31, "$2.10",
                38, TimeSpan.FromHours(4), 27, TimeSpan.FromDays(4))),

        new DemoUsageProvider("gemini", "Gemini", "G", "#4285F4",
            new DemoUsageProvider.DemoValues("gemini-2.5-pro", 3_600, 22_000, 310_000, 14, "$0.90",
                21, TimeSpan.FromHours(9), 12, TimeSpan.FromDays(4))),

        new DemoUsageProvider("copilot", "Copilot", "⊕", "#8957E5",
            new DemoUsageProvider.DemoValues("gpt-4.1", 5_900, 40_000, 540_000, 88, "",
                91, TimeSpan.FromHours(1), 88, TimeSpan.FromDays(4))),
    ];

    /// <summary>
    /// Fixture de config : un fournisseur masqué et un non détecté, pour que les badges et les états
    /// apparaissent sur la capture de la fenêtre de réglages — des états que la machine de
    /// développement ne produit pas d'elle-même.
    /// </summary>
    private static UsageConfig FixtureConfig() => new()
    {
        Enabled = true,
        AlertThreshold = 15,
        ShowCost = true,
        DefaultProviderId = "claude",
        Providers =
        {
            new AiProviderEntry { Id = "claude",  Name = "Claude",  DetectedName = "Claude",
                                  DataPath = @"C:\Users\Demo\.claude\projects", Detected = true, Order = 0 },
            new AiProviderEntry { Id = "codex",   Name = "Codex",   DetectedName = "Codex",
                                  DataPath = @"C:\Users\Demo\.codex\sessions", Detected = true, Order = 1 },
            new AiProviderEntry { Id = "gemini",  Name = "Gemini",  DetectedName = "Gemini",
                                  DataPath = @"C:\Users\Demo\.gemini\tmp", Detected = true, Order = 2 },
            new AiProviderEntry { Id = "copilot", Name = "Copilot", DetectedName = "Copilot",
                                  Detected = false, Order = 3 },
            new AiProviderEntry { Id = "demo",    Name = "Démo",    DetectedName = "Démo",
                                  Hidden = true, Detected = true, Order = 4 },
        },
    };

    private static void CaptureThenExit(Window win, string outPath, string target)
    {
        // DispatcherTimer : le délai et la capture restent sur le thread UI. Sans
        // SynchronizationContext (Dispatcher.Run brut), un await repartirait sur le thread pool et
        // les appels WPF échoueraient.
        var timer = new System.Windows.Threading.DispatcherTimer
        { Interval = TimeSpan.FromMilliseconds(700) };

        timer.Tick += (_, _) =>
        {
            timer.Stop();
            try
            {
                win.UpdateLayout();
                // GetDpi plutôt que PresentationSource.FromVisual, nul dans ce contexte hébergé.
                double scale = VisualTreeHelper.GetDpi(win).DpiScaleX;

                // On rend l'élément de contenu, pas la fenêtre : sur une fenêtre sans chrome
                // (WindowStyle=None) le rendu du Window lui-même sort transparent.
                var visual = win.Content as FrameworkElement ?? (FrameworkElement)win;
                double width = visual.ActualWidth > 0 ? visual.ActualWidth : win.ActualWidth;
                double height = visual.ActualHeight > 0 ? visual.ActualHeight : win.ActualHeight;
                Save(visual, width, height, outPath, target);
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
    }

    /// <summary>Rend un élément en PNG. Le DPI vient de VisualTreeHelper : PresentationSource
    /// est nul dans ce contexte hébergé.</summary>
    private static void Save(Visual visual, double width, double height, string outPath, string target)
    {
        double scale = VisualTreeHelper.GetDpi(visual).DpiScaleX;
        var rtb = new RenderTargetBitmap(
            (int)Math.Ceiling(width * scale), (int)Math.Ceiling(height * scale),
            96.0 * scale, 96.0 * scale, PixelFormats.Pbgra32);
        rtb.Render(visual);

        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(rtb));
        Directory.CreateDirectory(Path.GetDirectoryName(outPath)!);
        using (var fs = File.Create(outPath)) encoder.Save(fs);

        Console.WriteLine($"capturé : {outPath} ({target})");
    }
}
