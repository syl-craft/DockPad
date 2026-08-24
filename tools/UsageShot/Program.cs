using System.IO;
using System.Threading;
using System.Threading.Tasks;
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
/// <para>
/// <b>Limite connue : la cible <c>window</c> ne rend plus.</b> Le processus tourne à 80 % d'un cœur
/// et le dispatcher n'exécute plus rien — ni <c>ContentRendered</c>, ni un <c>DispatcherTimer</c>
/// posé après <c>Show</c> — ce qui décrit une boucle de mise en page ou d'invalidation qui affame
/// les priorités inférieures. Reproduit y compris au commit où cette cible produisait encore une
/// image correcte, donc la cause n'est pas dans la mise en page du bandeau : elle est à chercher
/// dans ce que <c>QuickAccessWindow</c> déclenche hors d'une vraie application (hotkey global,
/// hook WndProc, PopulateGrid). Les cibles <c>panel</c> et <c>config</c> fonctionnent.
/// </para>
/// </remarks>
internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        if (args.Length < 2)
        {
            Console.WriteLine("usage : UsageShot <panel|panel-tabs|panel-idle|panel-loading|panel-quota|window|window-off|window-unlocked|config> <cheminPng>");
            return;
        }

        string target = args[0];
        string outPath = Path.GetFullPath(args[1]);

        // Doit précéder toute utilisation des services : AppPaths ne lit la variable qu'une seule
        // fois, à la première résolution de ProfileRoot.
        var fixtureDir = Path.Combine(Path.GetTempPath(), "dockpad-usageshot");
        Directory.CreateDirectory(fixtureDir);
        Environment.SetEnvironmentVariable(AppPaths.OverrideVariable, fixtureDir);

        // « panel-tabs » rend un second fournisseur visible : le cas par défaut n'a qu'un seul
        // fournisseur, et c'est celui qu'il faut montrer en premier.
        var fixture = FixtureConfig(secondVisible: target is "panel-tabs" or "panel-idle",
                                    idle: target == "panel-idle");
        // « window-off » : bandeau désactivé, pour vérifier qu'il ne laisse aucune place derrière lui.
        if (target == "window-off") fixture.Enabled = false;
        UsageConfigService.Save(fixture);

        // La fenêtre entière se juge avec une grille remplie : vide, elle ne montre ni les icônes,
        // ni les bandes de couleur des types, ni la pagination.
        if (target is "window" or "window-off" or "window-unlocked") WriteDemoGrid();

        var app = new App();
        app.InitializeComponent();
        app.ShutdownMode = ShutdownMode.OnExplicitShutdown;
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


        // Le bandeau seul est rendu hors écran : une fenêtre à SizeToContent mesure avant que les
        // données arrivent et n'en reprend pas la hauteur, ce qui rognait la capture. Measure et
        // Arrange explicites donnent des dimensions déterministes.
        if (target is "panel" or "panel-tabs" or "panel-idle" or "panel-loading" or "panel-quota")
        {
            CapturePanel(outPath, loading: target == "panel-loading", idle: target == "panel-idle",
                         quotaLost: target == "panel-quota");
            return;
        }

        if (target is "window" or "window-off" or "window-unlocked")
        {
            CaptureWindow(outPath, unlockTiles: target == "window-unlocked");
            return;
        }

        Window win;
        switch (target)
        {
            case "config":
                win = new UsageConfigDialog { WindowStartupLocation = WindowStartupLocation.CenterScreen };
                break;


            default:
                throw new ArgumentException($"cible inconnue : {target} (panel | panel-tabs | panel-idle | panel-loading | panel-quota | window | window-off | window-unlocked | config)");
        }

        // Show + Dispatcher.Run : ShowDialog retourne immédiatement ici (Application jamais Run).
        // La capture est posée après Show et non sur ContentRendered — voir la remarque de classe.
        win.Show();
        CaptureThenExit(win, outPath, target);
        System.Windows.Threading.Dispatcher.Run();
    }

    /// <summary>
    /// Rend le contenu de <c>QuickAccessWindow</c> sans l'afficher, pour juger l'intégration du
    /// bandeau — alignement sur les tuiles, écart vertical, hauteur.
    /// </summary>
    /// <remarks>
    /// Sans <c>Show()</c> : afficher cette fenêtre dans un hôte qui n'est pas une vraie application
    /// déclenche une boucle qui affame le dispatcher, et rien ne se rend jamais. Mesurer et arranger
    /// son contenu contourne l'instanciation de la fenêtre. Le ViewModel de production est remplacé
    /// par la fixture avant toute lecture : sinon la capture embarquerait la consommation réelle.
    /// </remarks>
    private static void CaptureWindow(string outPath, bool unlockTiles = false)
    {
        var quick = new QuickAccessWindow();

        // Le verrou des tuiles n'a pas d'autre déclencheur que son bouton : on lève son événement
        // Click, plutôt que d'ouvrir l'état en public pour le seul besoin de la capture.
        if (unlockTiles && quick.FindName("TileLockButton") is Button lockButton)
            lockButton.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));

        // Largeur lue sur la fenêtre ; la hauteur vient de la mesure, la fenêtre étant en
        // SizeToContent (sa propriété Height vaut NaN).
        double width = quick.Width;
        quick.UsageBannerPanel.ViewModel = FixtureViewModel();
        quick.UsageBannerPanel.Start();

        var root = (FrameworkElement)quick.Content;
        // Deux passages : la géométrie du bandeau est posée au premier LayoutUpdated, donc APRÈS la
        // première mesure. Sans le second passage, la hauteur retenue est celle d'avant et la barre
        // de pagination sort de l'image. La vraie fenêtre, elle, en SizeToContent, se réajuste
        // d'elle-même.
        double height = 0;
        for (int pass = 0; pass < 2; pass++)
        {
            root.Measure(new Size(width, double.PositiveInfinity));
            height = root.DesiredSize.Height;
            root.Arrange(new Rect(0, 0, width, height));
            root.UpdateLayout();
        }

        Save(root, width, height, outPath, "window");
    }

    /// <summary>Rend le bandeau seul, sur le fond de la grille, sans passer par une fenêtre.</summary>
    private static void CapturePanel(string outPath, bool loading = false, bool idle = false,
                                     bool quotaLost = false)
    {
        // 698 = largeur réelle du bandeau dans la fenêtre : le bloc de tuiles (6 × 118) moins les
        // 10 px de marges horizontales d'une tuile, pour affleurer leurs bords visibles. Capturer
        // plus large donnerait une image flatteuse mais irréaliste — c'est à cette largeur que les
        // jauges se serrent.
        const double width = 698;   // 900 de bandeau + 24 de marge de chaque côté

        var panel = new UsagePanel { ViewModel = FixtureViewModel(loading, idle, quotaLost) };
        var frame = new Border
        {
            Padding = new Thickness(24),
            Background = new SolidColorBrush(Color.FromRgb(0xF3, 0xF3, 0xF3)),
            Child = panel,
        };

        // Les données d'abord : sans elles le bandeau est Collapsed et la mesure donne zéro.
        panel.Start();

        // État d'attente : une seconde lecture est lancée et ne rend pas la main, alors que les
        // valeurs de la première sont déjà affichées. C'est le seul moment où le sablier se voit.
        if (loading) _ = panel.ViewModel!.RefreshAsync();

        frame.Measure(new Size(width, double.PositiveInfinity));
        frame.Arrange(new Rect(0, 0, width, frame.DesiredSize.Height));
        frame.UpdateLayout();

        Save(frame, frame.ActualWidth, frame.ActualHeight, outPath, "panel");
    }

    /// <summary>
    /// ViewModel alimenté par des fournisseurs de démonstration seulement. C'est la raison d'être de
    /// la liste injectable de <see cref="UsageService"/> : le registre de production reste intact.
    /// </summary>
    private static UsageViewModel FixtureViewModel(bool loading = false, bool idle = false,
                                                   bool quotaLost = false)
    {
        var providers = FixtureProviders(idle, quotaLost);
        if (loading) providers = providers.Select(p => (IUsageProvider)new SlowAfterFirstRead(p)).ToList();
        return new UsageViewModel(new UsageService(providers), UsageConfigService.Load);
    }

    /// <summary>
    /// Fournisseur dont les jetons sont lus mais dont le quota est refusé par l'endpoint.
    /// </summary>
    /// <remarks>
    /// C'est l'état vécu en usage réel : un <c>HTTP 429</c> et les deux jauges disparaissent. La
    /// capture vérifie que leur place n'est plus un vide muet, et que la notice tient sur la ligne
    /// à la largeur réelle du bandeau.
    /// </remarks>
    private sealed class QuotaLostProvider : IUsageProvider
    {
        public string Id => "claude";
        public string Name => "Claude";

        public AiProbe Probe() => new()
        {
            Available = true, DisplayName = "Claude", Glyph = "✳", AccentColor = "#D97757",
        };

        public Task<AiUsage?> ReadAsync(CancellationToken ct) => Task.FromResult<AiUsage?>(new AiUsage
        {
            ProviderId = "claude", Name = "Claude", Glyph = "✳", AccentColor = "#D97757",
            UsageUrl = "https://claude.ai/new#settings/usage",
            Model = "claude-opus-5", Cost = "$4",
            SessionTokens = 12_400, DayTokens = 86_000, MonthTokens = 1_200_000, Requests = 47,
            QuotaNotice = "Quota indisponible — nouvelle tentative dans 4 min",
            QuotaNoticeNote = "HTTP 429 TooManyRequests. Les jauges restent masquées ; les "
                            + "métriques de jetons, lues dans les transcripts locaux, sont exactes.",
        });
    }

    /// <summary>
    /// Fournisseur détecté mais inactif sur la période : tout à zéro, aucune fenêtre de quota.
    /// </summary>
    /// <remarks>
    /// C'est l'état d'un assistant installé qu'on n'a pas utilisé ce mois-ci. Il garde son onglet
    /// plutôt que de disparaître du bandeau — disparaître doit vouloir dire « pas installé », et
    /// rien d'autre. La capture sert à vérifier que cet onglet reste lisible : des tirets là où la
    /// valeur n'a pas de sens, des zéros là où zéro est la mesure.
    /// </remarks>
    private sealed class IdleProvider(string id, string name, string glyph, string accent) : IUsageProvider
    {
        public string Id => id;
        public string Name => name;

        public AiProbe Probe() => new()
        {
            Available = true, DisplayName = name, Glyph = glyph, AccentColor = accent,
        };

        public Task<AiUsage?> ReadAsync(CancellationToken ct) => Task.FromResult<AiUsage?>(new AiUsage
        {
            ProviderId = id, Name = name, Glyph = glyph, AccentColor = accent,
        });
    }

    /// <summary>
    /// Rapide au premier appel, puis ne rend plus la main : c'est ce qui permet de capturer l'état
    /// d'attente avec des valeurs déjà affichées, comme lors d'un rafraîchissement réel.
    /// </summary>
    private sealed class SlowAfterFirstRead(IUsageProvider inner) : IUsageProvider
    {
        private bool _served;

        public string Id => inner.Id;
        public string Name => inner.Name;
        public AiProbe Probe() => inner.Probe();

        public async Task<AiUsage?> ReadAsync(CancellationToken ct)
        {
            if (_served) await Task.Delay(TimeSpan.FromMinutes(5), ct);
            _served = true;
            return await inner.ReadAsync(ct);
        }
    }

    /// <summary>
    /// Quatre fournisseurs plausibles. Copilot ne rapporte pas de coût (forfait) et n'a pas de
    /// quota hebdomadaire connu : la capture montre ainsi les deux cas dégradés du bandeau.
    /// </summary>
    private static List<IUsageProvider> FixtureProviders(bool idle = false, bool quotaLost = false) =>
    [
        quotaLost
            ? new QuotaLostProvider()
            : new DemoUsageProvider("claude", "Claude", "✳", "#D97757",
                new DemoUsageProvider.DemoValues("claude-opus-5", 12_400, 86_000, 1_200_000, 47, "$4",
                    62, TimeSpan.FromHours(2) + TimeSpan.FromMinutes(40), 44, TimeSpan.FromDays(4),
                    UsageUrl: "https://claude.ai/new#settings/usage")),

        idle
            ? new IdleProvider("codex", "Codex", "C", "#10A37F")
            : new DemoUsageProvider("codex", "Codex", "C", "#10A37F",
                new DemoUsageProvider.DemoValues("gpt-5-codex", 8_100, 54_000, 760_000, 31, "$2",
                    38, TimeSpan.FromHours(4), 27, TimeSpan.FromDays(4),
                    UsageUrl: "https://platform.openai.com/usage")),

        new DemoUsageProvider("gemini", "Gemini", "G", "#4285F4",
            new DemoUsageProvider.DemoValues("gemini-2.5-pro", 3_600, 22_000, 310_000, 14, "$1",
                21, TimeSpan.FromHours(9), 12, TimeSpan.FromDays(4))),

        new DemoUsageProvider("copilot", "Copilot", "⊕", "#8957E5",
            new DemoUsageProvider.DemoValues("gpt-4.1", 5_900, 40_000, 540_000, 88, "",
                91, TimeSpan.FromHours(1), 88, TimeSpan.FromDays(4))),
    ];

    /// <summary>
    /// Grille de démonstration : des tuiles des cinq types, sur trois pages.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Les icônes sont extraites d'exécutables <b>réellement présents</b> sur la machine, choisis
    /// dans une liste de candidats — même patron que <c>PresetService</c>. Rien n'est embarqué dans
    /// le dépôt : aucune icône tierce à redistribuer, et une machine sans Chrome ni VS Code produit
    /// la même capture, en moins garnie.
    /// </para>
    /// <para>
    /// Les noms sont des noms de produit ou des chemins : ils ne se traduisent pas, la capture sert
    /// donc aux deux langues.
    /// </para>
    /// <para>
    /// Quelques cases restent vides à dessein — le « + » grisé fait partie de ce qu'il faut montrer.
    /// </para>
    /// </remarks>
    /// <summary>Dossier des icônes de démonstration, surchargeable par variable d'environnement.</summary>
    private static string DemoIcons =>
        Environment.GetEnvironmentVariable("DOCKPAD_DEMO_ICONS") ?? @"C:\dev\Dock-icons";

    /// <summary>
    /// Icône du jeu de démonstration, ou chaîne vide si le dossier n'est pas là.
    /// </summary>
    /// <remarks>
    /// Ces PNG vivent <b>hors du dépôt</b> et volontairement : ce sont des logos de produits, qu'on
    /// ne redistribue pas dans un dépôt public. Sur une machine qui n'a pas le dossier, la tuile
    /// s'affiche sans icône — la capture est moins jolie, elle n'est pas cassée.
    /// </remarks>
    private static string DemoIcon(string name)
    {
        var path = Path.Combine(DemoIcons, name + ".png");
        return File.Exists(path) ? path : "";
    }

    private static void WriteDemoGrid()
    {
        string local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        string sys = Environment.GetFolderPath(Environment.SpecialFolder.System);
        string win = Environment.GetFolderPath(Environment.SpecialFolder.Windows);

        string terminal = FirstExisting(
            Path.Combine(local, @"Microsoft\WindowsApps\wt.exe"),
            Path.Combine(sys, "cmd.exe"));
        string posh = Path.Combine(sys, @"WindowsPowerShell\v1.0\powershell.exe");
        string browser = FirstExisting(
            @"C:\Program Files\Google\Chrome\Application\chrome.exe",
            @"C:\Program Files (x86)\Microsoft\Edge\Application\msedge.exe");
        string moba = FirstExisting(
            @"C:\Program Files (x86)\Mobatek\MobaXterm\MobaXterm.exe",
            @"C:\Program Files\Mobatek\MobaXterm\MobaXterm.exe");
        string folderIcon = Path.Combine(AppContext.BaseDirectory, "Assets", "folder.png");

        // Le jeu d'icônes du projet, quand il est présent ; sinon l'icône de l'exe, sinon rien.
        string claude = Or(DemoIcon("claude-code"), terminal);
        string code = Or(DemoIcon("vscode"), FirstExisting(
            Path.Combine(local, @"Programs\Microsoft VS Code\Code.exe"),
            @"C:\Program Files\Microsoft VS Code\Code.exe"));
        string fusion = DemoIcon("fusion360");
        string bambu = DemoIcon("bambu-studio");
        string devops = DemoIcon("azure-devops");
        string prs = DemoIcon("pull-requests");
        string board = DemoIcon("task-board");
        string gmail = DemoIcon("gmail");

        var tiles = new List<ShortcutEntry>
        {
            // Outils de développement
            Tile(0, 0, 0, "Claude Code", ShortcutType.OpenTerminal, @"C:\dev\DockPad", claude),
            Tile(0, 0, 1, "VS Code", ShortcutType.RunCommand, @"code C:\dev\DockPad", code),
            Tile(0, 0, 2, "MobaXterm", ShortcutType.RunCommand, moba, Or(moba, "")),
            Tile(0, 0, 3, "PowerShell", ShortcutType.OpenTerminal, @"C:\dev", posh),
            Tile(0, 0, 4, "Fusion 360", ShortcutType.RunCommand, "fusion360.exe", fusion),
            Tile(0, 0, 5, "Bambu Studio", ShortcutType.RunCommand, "bambustudio.exe", bambu),

            // Dossiers
            Tile(0, 1, 0, @"C:\dev", ShortcutType.OpenFolder, @"C:\dev", folderIcon),
            Tile(0, 1, 1, "Projets", ShortcutType.OpenFolder, @"C:\dev\projets", folderIcon),
            Tile(0, 1, 2, "Impressions 3D", ShortcutType.OpenFolder, @"C:\dev\3d", folderIcon),
            Tile(0, 1, 3, "Documents", ShortcutType.OpenFolder, @"C:\Users\Demo\Documents", folderIcon),
            Tile(0, 1, 5, "Explorateur", ShortcutType.RunCommand, Path.Combine(win, "explorer.exe"),
                 Path.Combine(win, "explorer.exe")),

            // Web
            Tile(0, 2, 0, "Azure DevOps", ShortcutType.OpenUrl, "https://dev.azure.com", devops),
            Tile(0, 2, 1, "Pull requests", ShortcutType.OpenUrl, "https://github.com/pulls", prs),
            Tile(0, 2, 2, "Tableau", ShortcutType.OpenUrl, "https://dev.azure.com/_boards", board),
            Tile(0, 2, 3, "Gmail", ShortcutType.OpenUrl, "https://mail.google.com", gmail),
            // Pas de logo dans le jeu d'icones pour ces deux-la : repli sur l'icone du navigateur,
            // ce qui est exactement ce que fait DockPad pour une tuile OpenUrl sans icone propre.
            // Deposer figma.png / agenda-google.png dans le dossier d'icones suffit a les habiller.
            Tile(0, 2, 4, "Figma", ShortcutType.OpenUrl, "https://figma.com",
                 Or(DemoIcon("figma"), browser)),
            Tile(0, 2, 5, "Agenda", ShortcutType.OpenUrl, "https://calendar.google.com",
                 Or(DemoIcon("agenda-google"), browser)),

            // Système
            Tile(0, 3, 0, "Gestionnaire", ShortcutType.SwitchToProcess, "Taskmgr.exe",
                 Path.Combine(sys, "Taskmgr.exe")),
            Tile(0, 3, 1, "Bloc-notes", ShortcutType.RunCommand, Path.Combine(sys, "notepad.exe"),
                 Path.Combine(sys, "notepad.exe")),
        };

        ShortcutService.Save(tiles.Where(t => t.Command.Length > 0).ToList());

        // Trois pages : une seule ne montrerait pas la pagination, qui fait partie de la fenêtre.
        PageConfigService.Save(
        [
            new PageConfig { Index = 0 },
            new PageConfig { Index = 1 },
            new PageConfig { Index = 2 },
        ]);
    }

    /// <summary>Le premier des deux qui ne soit pas vide.</summary>
    private static string Or(string preferred, string fallback) =>
        preferred.Length > 0 ? preferred : fallback;

    private static ShortcutEntry Tile(int page, int row, int col, string name, ShortcutType type,
                                      string command, string icon) => new()
    {
        Page = page, Row = row, Col = col, Name = name, Type = type,
        Command = command, IconPath = icon,
        Terminal = type == ShortcutType.OpenTerminal
            ? new TerminalConfig { ExePath = icon, StartingDirectory = command }
            : null,
        ProcessSwitch = type == ShortcutType.SwitchToProcess
            ? new ProcessSwitchConfig { ProcessName = command, Executable = icon }
            : null,
    };

    /// <summary>Premier chemin existant, ou chaîne vide — la tuile est alors omise.</summary>
    private static string FirstExisting(params string[] candidates) =>
        candidates.FirstOrDefault(File.Exists) ?? "";

    /// <summary>
    /// Fixture de config : deux fournisseurs visibles seulement, plus des masqués et un non détecté.
    /// Deux visibles suffisent à montrer la mécanique d'onglets et laissent la place aux jauges à la
    /// largeur réelle du bandeau — quatre onglets les écrasaient. Les états masqué / non détecté /
    /// démo apparaissent tout de même sur la capture de la fenêtre de réglages, que la machine de
    /// développement ne produit pas d'elle-même.
    /// </summary>
    private static UsageConfig FixtureConfig(bool secondVisible, bool idle = false) => new()
    {
        Enabled = true,
        AlertThreshold = 15,
        ShowCost = true,
        // « panel-idle » sélectionne le fournisseur inactif : c'est lui qu'il faut voir.
        DefaultProviderId = idle ? "codex" : "claude",
        Providers =
        {
            new AiProviderEntry { Id = "claude",  Name = "Claude",  DetectedName = "Claude",
                                  DataPath = @"C:\Users\Demo\.claude\projects", Detected = true, Order = 0 },
            new AiProviderEntry { Id = "codex",   Name = "Codex",   DetectedName = "Codex",
                                  DataPath = @"C:\Users\Demo\.codex\sessions", Detected = true,
                                  Hidden = !secondVisible, Order = 1 },
            new AiProviderEntry { Id = "gemini",  Name = "Gemini",  DetectedName = "Gemini",
                                  DataPath = @"C:\Users\Demo\.gemini\tmp", Detected = true,
                                  Hidden = true, Order = 2 },
            new AiProviderEntry { Id = "copilot", Name = "Copilot", DetectedName = "Copilot",
                                  Detected = false, Hidden = true, Order = 3 },
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
