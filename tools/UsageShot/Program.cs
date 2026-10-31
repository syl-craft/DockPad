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
using DockPad.Services.StreamDock;
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
/// Mêmes pièges WPF que McpShot et BrowserShot : STAThread, App sans Run, DPI via VisualTreeHelper, un processus par
/// fenêtre. Tout se rend hors écran (Measure, Arrange, RenderTargetBitmap) : ni Show() ni Dispatcher.Run().
/// </para>
/// <para>
/// <b>Le vrai démarrage ne tourne jamais.</b> Le constructeur d'<c>Application</c> poste une opération qui lance
/// <c>OnStartup</c> dès qu'un dispatcher tourne. Aucune cible ne le fait tourner (rendu hors écran) et l'opération est
/// annulée à la construction, comme dans <c>DockPad.Tests.Wpf</c>.
/// </para>
/// <para>
/// <b>Barre à droite.</b> <c>bar-matrix</c> rend tous les cas et y passe <see cref="OverflowCheck"/>, <c>bar-geometry</c>
/// relève le critère 2, <c>window-bar-&lt;style&gt;</c> et <c>window-legacy</c> capturent une fenêtre seule.
/// </para>
/// </remarks>
internal static partial class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        if (args.Length < 2)
        {
            Console.WriteLine("usage : UsageShot <panel|panel-tabs|panel-codex|panel-codex-stale|panel-idle|panel-loading|panel-quota|window|window-off|window-unlocked|window-favorites|window-bar-columns|window-bar-hero|window-bar-rings|window-bar-reservoir|window-legacy|config> <cheminPng>\n       UsageShot <bar-matrix|bar-geometry> <dossier>");
            return;
        }

        // Les rapports portent des accents : sans cela une console UTF-8 les affiche en mojibake.
        Console.OutputEncoding = System.Text.Encoding.UTF8;

        string target = args[0];
        string outPath = Path.GetFullPath(args[1]);

        // Doit précéder toute utilisation des services : AppPaths ne lit la variable qu'une seule
        // fois, à la première résolution de ProfileRoot.
        var fixtureDir = Path.Combine(Path.GetTempPath(), "dockpad-usageshot");
        Directory.CreateDirectory(fixtureDir);
        Environment.SetEnvironmentVariable(AppPaths.OverrideVariable, fixtureDir);

        // « panel-tabs » rend un second fournisseur visible : le cas par défaut n'a qu'un seul
        // fournisseur, et c'est celui qu'il faut montrer en premier.
        var fixture = FixtureConfig(secondVisible: target is "panel-tabs" or "panel-idle" or "panel-codex" or "panel-codex-stale",
                                    idle: target is "panel-idle" or "panel-codex" or "panel-codex-stale");
        // « window-off » : bandeau désactivé, pour vérifier qu'il ne laisse aucune place derrière lui.
        if (target == "window-off") fixture.Enabled = false;
        // « window-bar-<style> » : barre à droite, trois fournisseurs à quota. Les valeurs sont fixes.
        var barStyle = BarStyleOf(target);
        if (barStyle != null) fixture = BarFixtureConfig(barStyle.Value);
        UsageConfigService.Save(fixture);
        // « window-legacy » : usage.json sans les cinq réglages de la barre, qui doit rendre comme « window ».
        if (target == "window-legacy") StripBarSettings();

        // La fenêtre entière se juge avec une grille remplie : vide, elle ne montre ni les icônes,
        // ni les bandes de couleur des types, ni la pagination.
        if (target is "window" or "window-legacy" or "window-off" or "window-unlocked" or "window-favorites" || barStyle != null) WriteDemoGrid();
        // La grille des favoris est un fichier à part : sans elle, « window-favorites » capturerait
        // une page vide, qui ne montre ni tuile, ni bande de couleur, ni pagination.
        if (target == "window-favorites") WriteDemoFavorites();

        // Le constructeur d'Application poste l'opération qui lancerait le vrai OnStartup : on l'annule.
        var isConstructing = true;
        System.Windows.Threading.Dispatcher.CurrentDispatcher.Hooks.OperationPosted += (_, e) =>
        {
            if (isConstructing) e.Operation.Abort();
        };
        var app = new App();
        isConstructing = false;
        app.InitializeComponent();
        // L'outil EST l'assembly d'entree, mais il montre les fenetres de DockPad : sans
        // cette pose, le pied de fenetre porterait la version de l'outil (v1.0.0).
        DockPad.Services.AppInfo.Initialize(typeof(App).Assembly);
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


        if (target == "bar-matrix")
        {
            WriteDemoGrid();
            Environment.ExitCode = RunBarMatrix(outPath);
            return;
        }

        if (target == "bar-geometry")
        {
            WriteDemoGrid();
            Environment.ExitCode = RunBarGeometry(outPath);
            return;
        }

        // Le bandeau seul est rendu hors écran : une fenêtre à SizeToContent mesure avant que les
        // données arrivent et n'en reprend pas la hauteur, ce qui rognait la capture. Measure et
        // Arrange explicites donnent des dimensions déterministes.
        if (target is "panel" or "panel-tabs" or "panel-codex" or "panel-codex-stale" or "panel-idle" or "panel-loading" or "panel-quota")
        {
            CapturePanel(outPath, loading: target == "panel-loading", idle: target == "panel-idle",
                         quotaLost: target == "panel-quota", codexQuota: target is "panel-codex" or "panel-codex-stale",
                         codexStale: target == "panel-codex-stale");
            return;
        }

        if (target is "window" or "window-legacy" or "window-off" or "window-unlocked" or "window-favorites" || barStyle != null)
        {
            CaptureWindow(outPath, unlockTiles: target == "window-unlocked",
                          favorites: target == "window-favorites", barStyle: barStyle);
            return;
        }

        if (target == "config")
        {
            CaptureDialog(new UsageConfigDialog(), outPath, target);
            return;
        }

        throw new ArgumentException($"Unknown target: {target} (panel | panel-tabs | panel-idle | panel-loading | panel-quota | window | window-legacy | window-off | window-unlocked | window-favorites | window-bar-columns | window-bar-hero | window-bar-rings | window-bar-reservoir | bar-matrix | bar-geometry | config)");
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
    private static void CaptureWindow(string outPath, bool unlockTiles = false, bool favorites = false,
                                      SideStripStyle? barStyle = null)
    {
        // Les relevés et la charge viennent de la fixture : ni les vrais fournisseurs ni le vrai processeur.
        var usage = barStyle == null ? FixtureViewModel() : BarViewModel();
        var quick = new QuickAccessWindow(usage, new SeriesLoadMonitor(DefaultLoad));
        quick.UsageBannerPanel.ViewModel = usage;
        quick.UsageBannerPanel.Start();
        // La fenêtre n'est pas affichée : sa charge ne démarrerait pas. La barre reçoit un flux déjà rempli.
        if (barStyle != null) quick.UsageBarPanel.ViewModel = new UsageBarViewModel(usage, FilledLoadFeed(DefaultLoad), () => BarNow);

        // Largeur lue sur la fenêtre APRÈS la mise en page : passer à droite l'agrandit de la barre et de son écart.
        double width = quick.Width;

        var root = (FrameworkElement)quick.Content;

        // Un premier passage AVANT de presser : une liaison ne s'évalue qu'à la mise en page, donc
        // la propriété Command des boutons est encore nulle sur une fenêtre jamais mesurée.
        root.Measure(new Size(width, double.PositiveInfinity));
        root.Arrange(new Rect(0, 0, width, root.DesiredSize.Height));
        root.UpdateLayout();

        // Le verrou et le mode n'ont pas d'autre déclencheur que leur bouton : on passe par lui,
        // plutôt que d'ouvrir leur état en public pour le seul besoin de la capture.
        if (unlockTiles) Press(quick, "TileLockButton");
        if (favorites) Press(quick, "TileModeButton");

        // Deux passages : la géométrie du bandeau est posée au premier LayoutUpdated, donc APRÈS la
        // première mesure. Sans le second passage, la hauteur retenue est celle d'avant et la barre
        // de pagination sort de l'image. La vraie fenêtre, elle, en SizeToContent, se réajuste
        // d'elle-même.
        double height = 0;
        for (int pass = 0; pass < 4; pass++)
        {
            width = quick.Width;
            root.Measure(new Size(width, double.PositiveInfinity));
            height = root.DesiredSize.Height;
            root.Arrange(new Rect(0, 0, width, height));
            root.UpdateLayout();
        }

        Save(root, width, height, outPath, "window");
    }

    // --- Barre à droite : fixture à valeurs fixes (Claude 62 % et 44 %, Codex 27 %, Antigravity 88 % et 18 %)

    /// <summary>
    /// Horloge fixe de toutes les fixtures : la barre, le bandeau et la fenêtre. Une heure de remise à zéro ne tourne pas.
    /// </summary>
    private static readonly DateTime BarNow = new(2026, 10, 30, 14, 0, 0);

    /// <summary>Mesures du processeur et de la mémoire, la plus ancienne en premier ; la dernière est celle qu'on lit.</summary>
    private static readonly int[] LoadCpu = [12, 18, 15, 30, 22, 19, 41, 35, 20, 17, 26, 23];
    private static readonly int[] LoadMemory = [58, 58, 59, 60, 60, 59, 61, 62, 61, 60, 61, 61];

    private static SideStripStyle? BarStyleOf(string target) => target switch
    {
        "window-bar-columns" => SideStripStyle.Columns,
        "window-bar-hero" => SideStripStyle.HeroStack,
        "window-bar-rings" => SideStripStyle.Rings,
        "window-bar-reservoir" => SideStripStyle.Reservoir,
        _ => null,
    };

    private static UsageConfig BarFixtureConfig(SideStripStyle style) => new()
    {
        Enabled = true,
        AlertThreshold = 15,
        DefaultProviderId = "claude",
        Placement = UsagePlacement.Right,
        BarStyle = style,
        Providers =
        {
            new AiProviderEntry { Id = "claude", Name = "Claude", DetectedName = "Claude", Detected = true, Order = 0 },
            new AiProviderEntry { Id = "codex", Name = "Codex", DetectedName = "Codex", Detected = true, Order = 1 },
            new AiProviderEntry { Id = "antigravity", Name = "Antigravity", DetectedName = "Antigravity", Detected = true, Order = 2 },
        },
    };

    private static UsageViewModel BarViewModel()
    {
        IUsageProvider[] providers =
        [
            new BarFixtureProvider("claude", "Claude", ProviderLogos.ClaudeAccent, "https://claude.ai/new#settings/usage",
                new UsageWindow { UsedPct = 62, ResetsAt = BarNow.AddHours(5.5) },
                new UsageWindow { UsedPct = 44, ResetsAt = BarNow.AddDays(3) }),
            new BarFixtureProvider("codex", "Codex", "#10A37F", "https://chatgpt.com/codex/cloud/settings/analytics#usage",
                null, new UsageWindow { UsedPct = 27, ResetsAt = BarNow.AddDays(3) }),
            new BarFixtureProvider("antigravity", "Antigravity", ProviderLogos.AntigravityAccent, "",
                new UsageWindow { UsedPct = 88, Label = "Claude · GPT", ResetsAt = BarNow.AddDays(1) },
                new UsageWindow { UsedPct = 18, Label = "Gemini", ResetsAt = BarNow.AddDays(1) }),
        ];
        return new UsageViewModel(new UsageService(providers), UsageConfigService.Load, () => BarNow);
    }

    /// <summary>Un fournisseur qui n'est pas la démonstration, donc éligible, aux valeurs fixes.</summary>
    private sealed class BarFixtureProvider(string id, string name, string accent, string url,
                                            UsageWindow? session, UsageWindow? week, string notice = "") : IUsageProvider
    {
        public string Id => id;
        public string Name => name;

        public AiProbe Probe() => new() { Available = true, DisplayName = name, Glyph = name[..1], AccentColor = accent };

        public Task<AiUsage?> ReadAsync(CancellationToken ct) => Task.FromResult<AiUsage?>(new AiUsage
        {
            ProviderId = id, Name = name, Glyph = name[..1], AccentColor = accent, UsageUrl = url,
            Session = session, Week = week, QuotaNotice = notice,
        });
    }

    private sealed class StepTimeProvider : TimeProvider
    {
        private StepTimer? _timer;

        public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period) =>
            _timer = new StepTimer(callback, state);

        public void Fire() => _timer?.Fire();

        private sealed class StepTimer(TimerCallback callback, object? state) : ITimer
        {
            public void Fire() => callback(state);
            public bool Change(TimeSpan dueTime, TimeSpan period) => true;
            public void Dispose() { }
            public ValueTask DisposeAsync() => ValueTask.CompletedTask;
        }
    }

    /// <summary>Rend le bandeau seul, sur le fond de la grille, sans passer par une fenêtre.</summary>
    private static void CapturePanel(string outPath, bool loading = false, bool idle = false,
                                     bool quotaLost = false, bool codexQuota = false, bool codexStale = false)
    {
        // Largeur du bandeau et marges extérieures de la capture.
        const double width = 698 + 2 * 24;

        var panel = new UsagePanel { ViewModel = FixtureViewModel(loading, idle, quotaLost, codexQuota, codexStale) };
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

        // Recalcule la géométrie après la création des gabarits.
        frame.InvalidateMeasure();
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
                                                   bool quotaLost = false, bool codexQuota = false,
                                                   bool codexStale = false)
    {
        var providers = FixtureProviders(idle, quotaLost);
        if (codexQuota) providers[1] = new CodexQuotaDemoProvider(codexStale);
        if (loading) providers = providers.Select(p => (IUsageProvider)new SlowAfterFirstRead(p)).ToList();
        return new UsageViewModel(new UsageService(providers), UsageConfigService.Load, () => BarNow);
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

    /// <summary>Quotas Codex de démonstration avec une seule fenêtre hebdomadaire.</summary>
    /// <param name="stale">Relevé vieux de 70 min : la jauge reste, datée.</param>
    private sealed class CodexQuotaDemoProvider(bool stale = false) : IUsageProvider
    {
        public string Id => "codex";
        public string Name => "Codex";

        public AiProbe Probe() => new()
        {
            Available = true, DisplayName = Name, Glyph = "C", AccentColor = "#10A37F", IsDemo = true,
        };

        public Task<AiUsage?> ReadAsync(CancellationToken ct) => Task.FromResult<AiUsage?>(new AiUsage
        {
            ProviderId = Id, Name = Name, Glyph = "C", AccentColor = "#10A37F", IsDemo = true,
            Model = "gpt-5-codex", SessionTokens = 8_100, DayTokens = 54_000,
            MonthTokens = 760_000, Requests = 31,
            Week = new UsageWindow
            {
                UsedPct = 27, ResetsAt = BarNow.AddDays(4),
                ObservedAt = stale ? BarNow.AddMinutes(-70) : null,
            },
            UsageUrl = "https://chatgpt.com/codex/cloud/settings/analytics#usage",
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
                    UsageUrl: "https://claude.ai/new#settings/usage"), () => BarNow),

        idle
            ? new IdleProvider("codex", "Codex", "C", "#10A37F")
            : new DemoUsageProvider("codex", "Codex", "C", "#10A37F",
                new DemoUsageProvider.DemoValues("gpt-5-codex", 8_100, 54_000, 760_000, 31, "$2",
                    38, TimeSpan.FromHours(4), 27, TimeSpan.FromDays(4),
                    UsageUrl: "https://chatgpt.com/codex/cloud/settings/analytics#usage"), () => BarNow),

        new DemoUsageProvider("gemini", "Gemini", "G", "#4285F4",
            new DemoUsageProvider.DemoValues("gemini-2.5-pro", 3_600, 22_000, 310_000, 14, "$1",
                21, TimeSpan.FromHours(9), 12, TimeSpan.FromDays(4)), () => BarNow),

        new DemoUsageProvider("copilot", "Copilot", "⊕", "#8957E5",
            new DemoUsageProvider.DemoValues("gpt-4.1", 5_900, 40_000, 540_000, 88, "",
                91, TimeSpan.FromHours(1), 88, TimeSpan.FromDays(4)), () => BarNow),
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

    /// <summary>
    /// Déclenche un bouton de la toolbar par sa <b>commande</b>, et non par son événement Click.
    /// </summary>
    /// <remarks>
    /// <c>RaiseEvent(ButtonBase.ClickEvent)</c> ne fait <b>pas</b> exécuter la <c>Command</c> liée :
    /// c'est <c>Button.OnClick()</c> qui s'en charge, et il est protégé. Depuis que les boutons de
    /// la toolbar sont des commandes, la cible <c>window-unlocked</c> capturait donc l'état par
    /// défaut — cadenas fermé — en prétendant montrer le verrou ouvert, sans le moindre message.
    /// Passer par la commande emprunte exactement le chemin de l'utilisateur.
    /// </remarks>
    private static void Press(QuickAccessWindow window, string buttonName)
    {
        if (window.FindName(buttonName) is not Button button)
            throw new InvalidOperationException($"bouton introuvable : {buttonName}");
        if (button.Command is not { } command)
            throw new InvalidOperationException($"bouton sans commande : {buttonName}");

        command.Execute(button.CommandParameter);
    }

    /// <summary>
    /// Une grille de favoris de démonstration : des sites, ce que le mode Favoris contient
    /// naturellement.
    /// </summary>
    /// <remarks>
    /// Les icônes viennent du jeu de PNG du projet quand il est là, et retombent sinon sur celle du
    /// navigateur — exactement ce que fait DockPad pour une tuile web sans icône propre. Aucune
    /// n'est embarquée dans le dépôt : ce sont des logos de produits.
    /// </remarks>
    private static void WriteDemoFavorites()
    {
        string browser = FirstExisting(
            @"C:\Program Files\Google\Chrome\Application\chrome.exe",
            @"C:\Program Files (x86)\Microsoft\Edge\Application\msedge.exe");

        var favorites = new List<ShortcutEntry>
        {
            Tile(0, 0, 0, "github.com", ShortcutType.OpenUrl,
                 "https://github.com/anthropics/claude-code/pull/1234", Or(DemoIcon("github"), browser)),
            Tile(0, 0, 1, "dev.azure.com", ShortcutType.OpenUrl,
                 "https://dev.azure.com/demo/_boards", Or(DemoIcon("azure-devops"), browser)),
            Tile(0, 0, 2, "mail.google.com", ShortcutType.OpenUrl,
                 "https://mail.google.com/mail/u/0", Or(DemoIcon("gmail"), browser)),
            Tile(0, 0, 3, "figma.com", ShortcutType.OpenUrl,
                 "https://figma.com/files/recent", Or(DemoIcon("figma"), browser)),
            Tile(0, 1, 0, "learn.microsoft.com", ShortcutType.OpenUrl,
                 "https://learn.microsoft.com/dotnet/desktop/wpf", browser),
            Tile(0, 1, 1, "localhost:44351", ShortcutType.OpenUrl,
                 "https://localhost:44351/admin", browser),
        };

        ShortcutService.Save(favorites.Where(t => t.Command.Length > 0).ToList(),
                             TileStore.EntriesPath(TileTarget.Favorites));

        // Deux pages : les favoris ont leur PROPRE pagination, et c'est la moitié de ce que la
        // capture doit montrer — la barre du bas ne suit pas celle des raccourcis.
        PageConfigService.Save(
        [
            new PageConfig { Index = 0 },
            new PageConfig { Index = 1 },
        ], TileStore.PagesPath(TileTarget.Favorites));
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
        // Section « Agent local (Ollama) » remplie : une capture aux champs vides ne la montrerait pas.
        OllamaHarness = "claude",
        OllamaModel = "ministral-3:3b",
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

    /// <summary>
    /// Rend un dialogue hors écran, comme <c>DialogShot</c> : ni <c>Show()</c> ni dispatcher, donc jamais le vrai <c>OnStartup</c>.
    /// </summary>
    private static void CaptureDialog(Window win, string outPath, string target)
    {
        var width = double.IsNaN(win.Width) ? 900 : win.Width;
        var root = (FrameworkElement)win.Content;

        // Deux passages : certaines géométries se posent au premier LayoutUpdated, donc après la première mesure.
        double height = 0;
        for (var pass = 0; pass < 2; pass++)
        {
            root.Measure(new Size(width, double.PositiveInfinity));
            height = root.DesiredSize.Height;
            root.Arrange(new Rect(0, 0, width, height));
            root.UpdateLayout();
        }

        Save(root, width, height, outPath, target);
    }

    /// <summary>
    /// Retire de usage.json les cinq réglages de la barre à droite.
    /// </summary>
    private static void StripBarSettings()
    {
        var path = Path.Combine(AppPaths.ProfileRoot, "usage.json");
        if (System.Text.Json.Nodes.JsonNode.Parse(File.ReadAllText(path)) is not System.Text.Json.Nodes.JsonObject document)
            throw new InvalidOperationException("usage.json is not a JSON object.");
        foreach (var key in new[] { "placement", "barStyle", "focusMode", "showSystemLoad", "showLoadHistory" })
        {
            var found = document.Where(pair => string.Equals(pair.Key, key, StringComparison.OrdinalIgnoreCase))
                                .Select(pair => pair.Key).ToList();
            if (found.Count == 0) throw new InvalidOperationException($"The setting {key} is not in usage.json.");
            foreach (var name in found) document.Remove(name);
        }
        File.WriteAllText(path, document.ToJsonString());
    }

    /// <summary>Rend un élément en PNG. Le DPI vient de VisualTreeHelper : PresentationSource
    /// est nul dans ce contexte hébergé.</summary>
    /// <summary>Basculer le theme APRES construction, comme le fait l'utilisateur.</summary>
    private static bool _switchAfterBuild;

    private static void Save(Visual visual, double width, double height, string outPath, string target, bool quiet = false)
    {
        if (_switchAfterBuild)
        {
            DockPad.Services.ThemeService.Apply(dark: true);
            if (visual is FrameworkElement element)
            {
                element.Measure(new Size(width, height));
                element.Arrange(new Rect(0, 0, width, height));
                element.UpdateLayout();
            }
        }

        double scale = VisualTreeHelper.GetDpi(visual).DpiScaleX;
        var rtb = new RenderTargetBitmap(
            (int)Math.Ceiling(width * scale), (int)Math.Ceiling(height * scale),
            96.0 * scale, 96.0 * scale, PixelFormats.Pbgra32);
        // Le fond de la fenetre avant son contenu : on rend l'element de contenu, qui n'a pas de
        // fond a lui. Sans cela la capture sort transparente derriere le texte, et un titre clair
        // compose sur noir se lit comme un titre sombre — piege verifie pendant la mise au point
        // du theme sombre.
        if (visual is DependencyObject node && Window.GetWindow(node)?.Background is { } backdrop)
        {
            var canvas = new DrawingVisual();
            using (var dc = canvas.RenderOpen())
                dc.DrawRectangle(backdrop, null, new Rect(0, 0, width, height));
            rtb.Render(canvas);
        }

        rtb.Render(visual);

        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(rtb));
        Directory.CreateDirectory(Path.GetDirectoryName(outPath)!);
        using (var fs = File.Create(outPath)) encoder.Save(fs);

        if (quiet == false) Console.WriteLine($"capturé : {outPath} ({target})");
    }
}
