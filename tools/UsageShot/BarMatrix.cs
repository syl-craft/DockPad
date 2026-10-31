using System.IO;
using System.Windows;
using System.Windows.Controls;
using DockPad;
using DockPad.Models;
using DockPad.Services;
using DockPad.Services.Localization;
using DockPad.Services.StreamDock;
using DockPad.Services.Usage;
using DockPad.Views;

namespace UsageShot;

/// <summary>
/// La barre dans la vraie fenêtre principale, sur tous les cas du critère 4 : quatre styles, deux thèmes, français et
/// pseudo-langue, bascule de thème à chaud. Chaque rendu passe par <see cref="OverflowCheck"/>.
/// </summary>
internal static partial class Program
{
    private static readonly SideStripStyle[] BarStyles =
        [SideStripStyle.Columns, SideStripStyle.HeroStack, SideStripStyle.Rings, SideStripStyle.Reservoir];

    /// <summary>
    /// Mesures de charge où le processeur n'est pas encore connu : une seule, donc « — ».
    /// </summary>
    private static readonly SystemLoad[] CpuUnknownLoad = [new(null, 58)];

    /// <summary>
    /// Une propriété : l'ordre d'initialisation des champs statiques entre deux fichiers n'est pas défini.
    /// </summary>
    private static SystemLoad[] DefaultLoad =>
        [.. LoadCpu.Zip(LoadMemory, (cpu, memory) => new SystemLoad(cpu, memory))];

    /// <summary>
    /// Un cas de la matrice : ce que les fournisseurs rendent, et ce que la config règle.
    /// </summary>
    private sealed record BarCase(string Name, Func<IUsageProvider[]> Providers, string DefaultId = "claude",
        bool ShowSystemLoad = true, SystemLoad[]? Load = null);

    private static UsageWindow Quota(int usedPct, TimeSpan? resetIn = null, string label = "", TimeSpan? age = null) => new()
    {
        UsedPct = usedPct,
        Label = label,
        ResetsAt = resetIn == null ? null : BarNow + resetIn.Value,
        ObservedAt = age == null ? null : BarNow - age.Value,
    };

    private static IUsageProvider Claude(UsageWindow? session, UsageWindow? week, string name = "Claude", string notice = "") =>
        new BarFixtureProvider("claude", name, ProviderLogos.ClaudeAccent, "https://claude.ai/new#settings/usage", session, week, notice);

    private static IUsageProvider Codex(UsageWindow? week, string name = "Codex") =>
        new BarFixtureProvider("codex", name, "#10A37F", "https://chatgpt.com/codex/cloud/settings/analytics#usage", null, week);

    private static IUsageProvider Antigravity(UsageWindow? first, UsageWindow? second, string name = "Antigravity") =>
        new BarFixtureProvider("antigravity", name, ProviderLogos.AntigravityAccent, "", first, second);

    private static readonly TimeSpan SessionReset = TimeSpan.FromHours(5.5);
    private static readonly TimeSpan WeekReset = TimeSpan.FromDays(3);

    /// <summary>
    /// Deux fournisseurs de démonstration : jamais éligibles, quelles que soient leurs fenêtres.
    /// </summary>
    private static IUsageProvider[] DemoOnly() =>
    [
        new DemoUsageProvider("gemini", "Gemini", "G", "#4285F4",
            new DemoUsageProvider.DemoValues("gemini-2.5-pro", 3_600, 22_000, 310_000, 14, "$1",
                21, TimeSpan.FromHours(9), 12, TimeSpan.FromDays(4)), () => BarNow),
        new DemoUsageProvider("copilot", "Copilot", "⊕", "#8957E5",
            new DemoUsageProvider.DemoValues("gpt-4.1", 5_900, 40_000, 540_000, 88, "",
                91, TimeSpan.FromHours(1), 88, TimeSpan.FromDays(4)), () => BarNow),
    ];

    /// <summary>
    /// Les cas du critère 4, dans l'ordre de la spec.
    /// </summary>
    private static readonly BarCase[] BarCases =
    [
        new("un-fournisseur", () => [Claude(Quota(62, SessionReset), Quota(44, WeekReset))]),
        new("deux-fournisseurs", () =>
            [Claude(Quota(62, SessionReset), Quota(44, WeekReset)), Codex(Quota(27, WeekReset))]),
        new("trois-fournisseurs", ThreeProviders),
        new("aucun-charge", DemoOnly, DefaultId: "gemini"),
        new("aucun-sans-charge", DemoOnly, DefaultId: "gemini", ShowSystemLoad: false),
        // Le gris d'un relevé ancien l'emporte sur l'alerte : 90 % ancien reste gris.
        new("releve-ancien", () =>
        [
            Claude(Quota(90, SessionReset, age: TimeSpan.FromMinutes(70)), Quota(44, WeekReset)),
            Codex(Quota(27, WeekReset, age: TimeSpan.FromMinutes(70))),
            Antigravity(Quota(88, WeekReset, "Claude · GPT"), Quota(18, WeekReset, "Gemini")),
        ]),
        // Seuil d'alerte à 15 % restant : 85 % consommé est exactement au seuil, 84 % juste dessous.
        new("au-seuil", () =>
        [
            Claude(Quota(85, SessionReset), Quota(44, WeekReset)),
            Codex(Quota(85, WeekReset)),
            Antigravity(Quota(84, WeekReset, "Claude · GPT"), Quota(18, WeekReset, "Gemini")),
        ]),
        new("reset-inconnu", () =>
        [
            Claude(Quota(62), Quota(44)),
            Codex(Quota(27)),
            Antigravity(Quota(88, label: "Claude · GPT"), Quota(18, label: "Gemini")),
        ]),
        new("mise-en-avant-en-panne", () =>
        [
            Claude(null, null, notice: "Quota Claude indisponible — nouvelle tentative dans 4 min"),
            Codex(Quota(27, WeekReset)),
            Antigravity(Quota(88, WeekReset, "Claude · GPT"), Quota(18, WeekReset, "Gemini")),
        ]),
        new("processeur-inconnu", ThreeProviders, Load: CpuUnknownLoad),
        new("noms-longs", () =>
        [
            Claude(Quota(62, SessionReset, "Fenêtre glissante étendue"), Quota(44, WeekReset), name: "Assistant de programmation internationale"),
            Codex(Quota(27, WeekReset, "Hebdomadaire de l'équipe"), name: "Fournisseur à nom vraiment très long"),
            Antigravity(Quota(88, WeekReset, "Claude · GPT · Gemini · Mistral"), Quota(18, WeekReset, "Un autre libellé interminable"),
                name: "Antigravity Enterprise Édition Mondiale"),
        ]),
        // L'IA mise en avant a deux fenêtres aux noms longs, comme Antigravity : la légende des Anneaux est réduite, et
        // son infobulle doit porter les deux noms complets.
        new("deux-fenetres-noms-longs", () =>
        [
            Antigravity(Quota(88, WeekReset, "Claude · GPT · Gemini · Mistral"), Quota(18, WeekReset, "Gemini Pro étendu"),
                name: "Antigravity Enterprise Édition Mondiale"),
            Codex(Quota(27, WeekReset)),
        ], DefaultId: "antigravity"),
        new("notice-longue", () =>
        [
            Claude(null, null, notice: "Quota indisponible pour ce fournisseur parce que le service distant ne répond plus — réessai dans 4 min"),
            Codex(Quota(27, WeekReset)),
        ]),
    ];

    private static IUsageProvider[] ThreeProviders() =>
    [
        Claude(Quota(62, SessionReset), Quota(44, WeekReset)),
        Codex(Quota(27, WeekReset)),
        Antigravity(Quota(88, WeekReset, "Claude · GPT"), Quota(18, WeekReset, "Gemini")),
    ];

    private static UsageConfig MatrixConfig(BarCase barCase, IUsageProvider[] providers, SideStripStyle style,
        UsagePlacement placement)
    {
        var config = new UsageConfig
        {
            Enabled = true,
            AlertThreshold = 15,
            DefaultProviderId = barCase.DefaultId,
            Placement = placement,
            BarStyle = style,
            ShowSystemLoad = barCase.ShowSystemLoad,
        };
        for (var order = 0; order < providers.Length; order++)
        {
            var name = providers[order].Name;
            config.Providers.Add(new AiProviderEntry
            {
                Id = providers[order].Id, Name = name, DetectedName = name, Detected = true, Order = order,
            });
        }
        return config;
    }

    /// <summary>
    /// Une fenêtre principale construite, mise en page, avec sa barre et ses dimensions.
    /// </summary>
    private sealed record BuiltWindow(QuickAccessWindow Quick, FrameworkElement Root, double Width, double Height)
        : IDisposable
    {
        /// <summary>
        /// Détache la fenêtre des événements statiques : Close() la masquerait seulement.
        /// </summary>
        public void Dispose() => Quick.DetachFromStatics();
    }

    private static BuiltWindow BuildBarWindow(BarCase barCase, SideStripStyle style, UsagePlacement placement)
    {
        var providers = barCase.Providers();
        var config = MatrixConfig(barCase, providers, style, placement);
        var usage = new UsageViewModel(new UsageService(providers), () => config, () => BarNow);

        // Sans contexte de synchronisation, la lecture de relevés immédiats se termine sur place : aucun dispatcher ne tourne.
        var refresh = usage.RefreshAsync();
        if (refresh.IsCompleted == false) throw new InvalidOperationException("The usage read did not complete in place.");

        var load = barCase.Load ?? DefaultLoad;
        var quick = new QuickAccessWindow(usage, new SeriesLoadMonitor(load));
        quick.UsageBannerPanel.ViewModel = usage;
        quick.UsageBannerPanel.Start();
        // La fenêtre n'est pas affichée : sa charge ne démarrerait pas. La barre reçoit un flux déjà rempli.
        quick.UsageBarPanel.ViewModel = new UsageBarViewModel(usage, FilledLoadFeed(load), () => BarNow);

        var built = new BuiltWindow(quick, (FrameworkElement)quick.Content, 0, 0);
        return LayOut(built);
    }

    /// <summary>
    /// Plusieurs passages : la barre se cale au LayoutUpdated qui suit la première mesure et élargit la fenêtre.
    /// </summary>
    private static BuiltWindow LayOut(BuiltWindow built)
    {
        double width = 0, height = 0;
        for (var pass = 0; pass < 4; pass++)
        {
            width = built.Quick.Width;
            built.Root.Measure(new Size(width, double.PositiveInfinity));
            height = built.Root.DesiredSize.Height;
            built.Root.Arrange(new Rect(0, 0, width, height));
            built.Root.UpdateLayout();
        }
        return built with { Width = width, Height = height };
    }

    /// <summary>
    /// Un moniteur qui rend la série dans l'ordre, puis sa dernière mesure.
    /// </summary>
    private sealed class SeriesLoadMonitor(IReadOnlyList<SystemLoad> series) : ISystemLoadMonitor
    {
        private int _next;

        public SystemLoad Sample() => series[Math.Min(_next++, series.Count - 1)];

        public void Reset() => _next = 0;
    }

    /// <summary>
    /// Flux de charge déjà rempli de la série : le minuteur est tiré à la main, une mesure après l'autre.
    /// </summary>
    /// <remarks>
    /// Chaque tic attend la fin complète de la mesure précédente (<c>PendingSample</c>, interne) : un tic tiré avant est
    /// sauté, un tic retiré après peut ajouter une mesure de trop.
    /// </remarks>
    private static SystemLoadFeed FilledLoadFeed(IReadOnlyList<SystemLoad> series)
    {
        var time = new StepTimeProvider();
        var feed = new SystemLoadFeed(new SeriesLoadMonitor(series), time, null);
        feed.Start();
        for (var count = 1; count <= series.Count; count++)
        {
            if (count > 1) time.Fire();
            if (feed.PendingSample.Wait(TimeSpan.FromSeconds(5)) == false)
                throw new InvalidOperationException($"Load sample {count} was not published.");
        }
        return feed;
    }

    /// <summary>
    /// Tous les cas sur tous les styles, thèmes et langues ; renvoie 1 au moindre écart de débordement.
    /// </summary>
    private static int RunBarMatrix(string outDir)
    {
        var violations = new List<OverflowCheck.Violation>();
        int renders = 0, texts = 0, shapes = 0;

        void Check(BuiltWindow built, string context)
        {
            var report = OverflowCheck.Inspect(built.Quick.UsageBarPanel, context);
            violations.AddRange(report.Violations);
            renders++;
            texts += report.TextCount;
            shapes += report.ShapeCount;
        }

        foreach (var (lang, tag) in new[] { ("fr", "fr"), ("pseudo", "qps-Ploc") })
        {
            Loc.SetCulture(Loc.Parse(tag));

            foreach (var dark in new[] { false, true })
            {
                ThemeService.Apply(dark);
                var theme = dark ? "dark" : "light";

                foreach (var style in BarStyles)
                {
                    foreach (var barCase in BarCases)
                    {
                        using var built = BuildBarWindow(barCase, style, UsagePlacement.Right);
                        Check(built, $"{style}/{barCase.Name}/{theme}/{lang}");
                        Save(built.Root, built.Width, built.Height,
                            Path.Combine(outDir, lang, theme, $"{style}-{barCase.Name}.png"), "barre", quiet: true);
                    }
                }
            }
        }

        // Bascule de thème à chaud, dans les deux sens : le rendu doit être celui d'une construction directe dans le thème
        // d'arrivée, octet pour octet. Sinon une couleur résolue au lieu d'une référence dynamique resterait invisible.
        Loc.SetCulture(Loc.Parse("fr"));
        var threeProviders = BarCases.Where(c => c.Name == "trois-fournisseurs").First();
        foreach (var toDark in new[] { true, false })
        {
            foreach (var style in BarStyles)
            {
                ThemeService.Apply(dark: toDark == false);
                var created = BuildBarWindow(threeProviders, style, UsagePlacement.Right);
                ThemeService.Apply(dark: toDark);
                using var built = LayOut(created);

                var direction = toDark ? "clair-vers-sombre" : "sombre-vers-clair";
                var context = $"{style}/trois-fournisseurs/bascule-{direction}";
                Check(built, context);
                var switchedPath = Path.Combine(outDir, "bascule", $"{style}-{direction}.png");
                Save(built.Root, built.Width, built.Height, switchedPath, "barre", quiet: true);

                using var direct = BuildBarWindow(threeProviders, style, UsagePlacement.Right);
                var directPath = Path.Combine(outDir, "bascule", "direct", $"{style}-{direction}.png");
                Save(direct.Root, direct.Width, direct.Height, directPath, "barre", quiet: true);
                if (File.ReadAllBytes(switchedPath).AsSpan().SequenceEqual(File.ReadAllBytes(directPath)) == false)
                {
                    violations.Add(new OverflowCheck.Violation(context, "UsageBar",
                        "le rendu après bascule diffère du rendu direct dans le thème d'arrivée"));
                }
            }
        }

        Console.WriteLine($"{renders} rendus contrôlés ({texts} textes, {shapes} formes), {violations.Count} écart(s)");
        foreach (var violation in violations) Console.WriteLine("  " + violation);
        return violations.Count == 0 ? 0 : 1;
    }

    /// <summary>
    /// Mesures du critère 2 sur les rendus À droite et En bas, dans les deux thèmes ; renvoie 1 si l'une sort de la
    /// tolérance (±1).
    /// </summary>
    private static int RunBarGeometry(string outDir)
    {
        Loc.SetCulture(Loc.Parse("fr"));
        var failures = 0;
        foreach (var dark in new[] { false, true })
        {
            var theme = dark ? "sombre" : "clair";
            Console.WriteLine($"Thème {theme} :");
            failures += MeasureGeometry(outDir, dark, theme);
        }
        return failures == 0 ? 0 : 1;
    }

    /// <summary>
    /// Les mesures d'un thème ; renvoie le nombre d'écarts.
    /// </summary>
    private static int MeasureGeometry(string outDir, bool dark, string theme)
    {
        ThemeService.Apply(dark);
        var barCase = BarCases.Where(c => c.Name == "trois-fournisseurs").First();

        using var bottom = BuildBarWindow(barCase, SideStripStyle.Columns, UsagePlacement.Bottom);
        using var right = BuildBarWindow(barCase, SideStripStyle.Columns, UsagePlacement.Right);
        Save(bottom.Root, bottom.Width, bottom.Height, Path.Combine(outDir, "geometrie", $"en-bas-{theme}.png"), "en bas", quiet: true);
        Save(right.Root, right.Width, right.Height, Path.Combine(outDir, "geometrie", $"a-droite-{theme}.png"), "à droite", quiet: true);

        var failures = 0;
        void Measure(string label, double actual, double expected, double tolerance = 1)
        {
            var ok = Math.Abs(actual - expected) <= tolerance;
            if (ok == false) failures++;
            Console.WriteLine($"  {(ok ? "ok    " : "ECART ")} {label} : {actual:0.##} (attendu {expected:0.##} ±{tolerance})");
        }

        var tiles = TileRects(right);
        var bar = right.Quick.UsageBarPanel;
        var barRect = Bounds(bar, right.Root);
        var first = tiles[0];
        var last = tiles[^1];
        var firstRowEnd = tiles.Where(rect => Math.Abs(rect.Top - first.Top) < 1).MaxBy(rect => rect.Right);
        var secondTile = tiles.Where(rect => Math.Abs(rect.Top - first.Top) < 1 && rect.Left > first.Left).MinBy(rect => rect.Left);
        var tileGap = secondTile.Left - first.Right;

        Console.WriteLine("Barre à droite :");
        Measure("largeur de la barre", barRect.Width, OverflowCheck.BarWidth, 0.5);
        Measure("haut de la barre sur le bord de la première rangée", barRect.Top, first.Top);
        Measure("bas de la barre sur le bord de la dernière rangée", barRect.Bottom, last.Bottom);
        Measure("écart barre-grille = écart entre deux tuiles", barRect.Left - firstRowEnd.Right, tileGap);

        // La pagination reste centrée sous les tuiles seules.
        var pagination = Bounds((FrameworkElement)right.Quick.FindName("PaginationBar"), right.Root);
        var tilesCenter = (first.Left + firstRowEnd.Right) / 2;
        Measure("centre de la pagination sous le centre des tuiles", (pagination.Left + pagination.Right) / 2, tilesCenter);

        var bottomTiles = TileRects(bottom);
        var bottomPagination = Bounds((FrameworkElement)bottom.Quick.FindName("PaginationBar"), bottom.Root);
        Measure("décalage horizontal de la première tuile (droite - bas)", first.Left - bottomTiles[0].Left, 0);
        Measure("pagination : position relative aux tuiles, droite - bas",
            (pagination.Left - first.Left) - (bottomPagination.Left - bottomTiles[0].Left), 0);

        Console.WriteLine($"Fenêtre : en bas {bottom.Width:0.##} × {bottom.Height:0.##}, à droite {right.Width:0.##} × {right.Height:0.##}");
        Measure("largeur gagnée = barre + écart", right.Width - bottom.Width, OverflowCheck.BarWidth + tileGap);

        // La fenêtre à droite a perdu le bandeau et sa marge, lus sur le rendu En bas.
        var banner = bottom.Quick.UsageBannerPanel;
        var bannerHeight = banner.ActualHeight + banner.Margin.Top + banner.Margin.Bottom;
        Measure("hauteur perdue = bandeau + marge", bottom.Height - right.Height, bannerHeight);

        return failures;
    }

    private static Rect Bounds(FrameworkElement element, FrameworkElement root) =>
        element.TransformToAncestor(root).TransformBounds(new Rect(element.RenderSize));

    /// <summary>
    /// Les bords visibles de chaque tuile rendue, dans les coordonnées de la fenêtre.
    /// </summary>
    private static List<Rect> TileRects(BuiltWindow built)
    {
        var grid = (ItemsControl)built.Quick.FindName("ShortcutsGrid");
        var rects = new List<Rect>();
        for (var index = 0; index < grid.Items.Count; index++)
        {
            if (grid.ItemContainerGenerator.ContainerFromIndex(index) is not ContentPresenter presenter
                || System.Windows.Media.VisualTreeHelper.GetChildrenCount(presenter) == 0
                || System.Windows.Media.VisualTreeHelper.GetChild(presenter, 0) is not FrameworkElement tile)
                continue;

            // Le rendu d'un élément exclut sa marge : c'est déjà le bord visible de la tuile.
            rects.Add(Bounds(tile, built.Root));
        }
        return rects;
    }
}
