using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using DockPad.Models;
using DockPad.Services.StreamDock;

namespace DockPad.Services.Usage;

/// <summary>
/// État affichable de la barre Usage IA à droite de la grille : ce que montre chacun des quatre styles, sans WPF.
/// Il projette les relevés du <see cref="UsageViewModel"/> et la charge du <see cref="SystemLoadFeed"/>.
/// </summary>
public sealed class UsageBarViewModel : INotifyPropertyChanged, IDisposable
{
    public const string StaleColor = "#8A8A8A";
    public const string CpuColor = "#F2C14E";
    public const string MemoryColor = "#B58CF2";

    private const string WarningGlyph = "⚠";
    private const string Ellipsis = "…";
    private const int QuotaColumnCount = 3;
    private const int OtherRingCount = 2;
    private const int QuotaDotCount = 2;
    private const int ColumnLabelMax = 3;
    private const int RingLabelMax = 5;
    private const int RowLabelMax = 7;
    private const int CardLabelMax = 8;
    private const int CardNameMax = 10;
    private const int NoticeTitleMax = 16;
    private const string NoticeTitleSeparator = " — ";

    /// <summary>Brosse du thème pour les nombres du processeur et de la mémoire : leurs couleurs pâlissent sur fond clair.</summary>
    private const string NeutralNumberKey = "Brush.Text";

    private readonly UsageViewModel _usage;
    private readonly SystemLoadFeed _feed;
    private readonly Func<DateTime> _clock;

    /// <summary>
    /// Éléments de quota du dernier recalcul complet : une mesure de charge les garde tels quels.
    /// </summary>
    private IReadOnlyList<BarColumn> _quotaColumns = [];
    private IReadOnlyList<BarRing> _quotaDots = [];

    /// <summary>
    /// Collections que la vue lie : leur instance ne change jamais, et un recalcul n'y remplace que les éléments qui
    /// ont changé. Les conteneurs, infobulles et menus ouverts des autres éléments restent en place.
    /// </summary>
    private readonly ObservableCollection<BarLogo> _logos = [];
    private readonly ObservableCollection<BarColumn> _columns = [];
    private readonly ObservableCollection<BarRing> _dots = [];
    private readonly ObservableCollection<BarRing> _loadRings = [];
    private string _focusProviderId = "";
    private BarGauge? _chosenGauge;
    private BarGauge? _previousGauge;

    /// <summary>
    /// Un relevé de fenêtre tel que la barre le montre : l'instantané, la ligne du planificateur et la fenêtre elle-même.
    /// </summary>
    private sealed record WindowView(AiUsage Usage, QuotaGauge Gauge, UsageWindow Window);

    public UsageBarViewModel(UsageViewModel usage, SystemLoadFeed load, Func<DateTime>? clock = null)
    {
        _usage = usage;
        _feed = load;
        _clock = clock ?? (() => DateTime.Now);

        _usage.PropertyChanged += OnUsageChanged;
        _feed.Changed += OnLoadChanged;
        Rebuild();
    }

    public BarState State { get; private set; }
    public IReadOnlyList<BarLogo> Logos => _logos;

    /// <summary>
    /// Au moins deux fournisseurs éligibles : la molette et Ctrl + Tab ont un autre fournisseur à atteindre.
    /// </summary>
    public bool CanCycle { get; private set; }

    public bool IsLoading { get; private set; }
    public bool IsAuto { get; private set; }

    /// <summary>« AUTO » en mode Auto non suspendu, vide sinon.</summary>
    public string AutoLabel { get; private set; } = "";

    /// <summary>« · AUTO », à la suite du nom court de la fenêtre mise en avant ; vide hors du mode Auto.</summary>
    public string AutoSuffix { get; private set; } = "";

    /// <summary>Notice courte de l'état Empty, vide dans les autres.</summary>
    public string EmptyText { get; private set; } = "";

    public SideStripStyle Style { get; private set; }
    public IReadOnlyList<BarColumn> Columns => _columns;
    public BarHeroCard? HeroCard { get; private set; }
    public IReadOnlyList<BarHeroRow> HeroRows { get; private set; } = [];
    public BarRings? Rings { get; private set; }
    public BarReservoir? Reservoir { get; private set; }
    public BarFeaturedGauge? FeaturedGauge { get; private set; }
    public IReadOnlyList<BarRing> LoadRings => _loadRings;

    /// <summary>La charge du PC, ou null quand la case est décochée.</summary>
    public BarLoad? Load { get; private set; }

    /// <summary>Style Héros, charge cochée et courbe cochée.</summary>
    public bool ShowCurve { get; private set; }

    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>
    /// Un clic sur un logo ou sur une ligne Héros : geste de sélection, qui suspend l'Auto. Sans effet sous deux
    /// fournisseurs éligibles.
    /// </summary>
    public void Pick(string providerId)
    {
        if (CanCycle == false) return;

        _usage.SelectByGesture(providerId);
    }

    /// <summary>Met cette fenêtre ou cette mesure en grand, sans écrire la sélection dans la configuration.</summary>
    public void PickGauge(BarGauge gauge)
    {
        var previous = FeaturedGauge?.Ring.Selection;
        var isLoad = gauge.Kind is BarGaugeKind.Cpu or BarGaugeKind.Memory;
        if (isLoad)
        {
            if (Load == null) return;
            if (CanCycle) _usage.SelectByGesture(_focusProviderId);
        }
        else
        {
            var usage = _usage.Snapshots
                .Where(snapshot => snapshot.ProviderId == gauge.ProviderId && UsageEligibility.IsEligible(snapshot))
                .FirstOrDefault();
            var window = gauge.Kind == BarGaugeKind.Session ? usage?.Session : usage?.Week;
            if (window == null) return;
            if (CanCycle) _usage.SelectByGesture(gauge.ProviderId);
        }

        // SelectByGesture publie le nouveau fournisseur et réinitialise son ancienne sélection avant ce choix précis.
        _chosenGauge = gauge;
        _previousGauge = previous;
        Rebuild();
    }

    /// <summary>Le double anneau du PC : CPU, mémoire, puis retour au quota le plus consommé.</summary>
    public void CycleLoadGauge()
    {
        if (Load == null) return;
        if (_chosenGauge?.Kind != BarGaugeKind.Memory)
        {
            PickGauge(new BarGauge("", _chosenGauge?.Kind == BarGaugeKind.Cpu ? BarGaugeKind.Memory : BarGaugeKind.Cpu));
            return;
        }

        if (CanCycle) _usage.SelectByGesture(_focusProviderId);
        _chosenGauge = null;
        _previousGauge = null;
        Rebuild();
    }

    /// <summary>La molette et Ctrl + Tab : fournisseur suivant (direction positive) ou précédent.</summary>
    public void Cycle(int direction) => _usage.Cycle(direction);

    public void Dispose()
    {
        _usage.PropertyChanged -= OnUsageChanged;
        _feed.Changed -= OnLoadChanged;
    }

    /// <summary>
    /// Rebuild du bandeau publie tout d'un bloc, <see cref="UsageViewModel.Snapshots"/> compris : un seul recalcul par relevé.
    /// </summary>
    private void OnUsageChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(UsageViewModel.Snapshots)) Rebuild();
        else if (e.PropertyName == nameof(UsageViewModel.IsLoading)) SyncLoading();
    }

    private void OnLoadChanged() => RebuildLoad();

    /// <summary>
    /// Une mesure de charge ne touche que la charge, les colonnes et pastilles du processeur et de la mémoire, et la
    /// courbe : les logos et les éléments de quota gardent leurs instances, sans notification.
    /// </summary>
    private void RebuildLoad()
    {
        if (Load == null) return;

        var load = BuildLoad();
        Load = load;
        SyncList(_columns, [.. _quotaColumns, CpuColumn(load), MemoryColumn(load)]);
        foreach (var ring in _loadRings) UpdateLoadRing(ring, load);
        if (Reservoir != null)
        {
            SyncList(_dots, [.. _quotaDots, .. _loadRings]);
            Reservoir = Reservoir with { Dots = _dots };
        }

        if (_chosenGauge?.Kind is BarGaugeKind.Cpu or BarGaugeKind.Memory && FeaturedGauge != null)
        {
            var ring = FeaturedGauge.Ring;
            UpdateLoadRing(ring, load);
            if (Reservoir != null)
                Reservoir = Reservoir with { Level = ring.Fraction, LevelText = ring.Text, Color = ring.Color, Tooltip = ring.Tooltip };
            if (Rings != null)
                Rings = Rings with { Outer = ring, CenterText = ring.Text, LegendTooltip = ring.Tooltip };
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(FeaturedGauge)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Rings)));
        }

        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Load)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Columns)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Reservoir)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(LoadRings)));
    }

    private void SyncLoading()
    {
        IsLoading = _usage.IsLoading;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsLoading)));
    }

    private void Rebuild()
    {
        var config = _usage.Config;
        var eligible = config.Enabled
            ? _usage.Snapshots.Where(UsageEligibility.IsEligible).ToList()
            : [];
        var focus = FindFocus(eligible);
        if (_focusProviderId != (focus?.ProviderId ?? ""))
        {
            _chosenGauge = null;
            _previousGauge = null;
        }
        _focusProviderId = focus?.ProviderId ?? "";
        var load = _feed.Current;

        var plan = SideStripPlanner.Plan(eligible, config, load, _clock(), focus?.ProviderId);
        var views = plan.Quotas.Select(quota => ViewOf(quota, eligible)).ToList();
        var focusViews = views.Where(view => view.Usage.ProviderId == focus?.ProviderId).ToList();
        var otherViews = views
            .Where(view => view.Usage.ProviderId != focus?.ProviderId)
            .GroupBy(view => view.Usage.ProviderId)
            .Select(group => group.First())
            .ToList();

        var loadItem = config.ShowSystemLoad ? BuildLoad() : null;
        var chosenIsAvailable = _chosenGauge?.Kind is BarGaugeKind.Cpu or BarGaugeKind.Memory
            ? loadItem != null
            : focusViews.Any(view => SelectionOf(view) == _chosenGauge);
        if (chosenIsAvailable == false)
        {
            _chosenGauge = null;
            _previousGauge = null;
        }

        State = eligible.Count > 0 ? BarState.Full : config.ShowSystemLoad ? BarState.LoadOnly : BarState.Empty;
        Style = config.BarStyle;
        IsLoading = _usage.IsLoading;
        IsAuto = _usage.IsAutoActive;
        AutoLabel = IsAuto ? Loc.T("UsageBar_Auto") : "";
        AutoSuffix = IsAuto ? Loc.F("UsageBar_AutoSuffix", AutoLabel) : "";
        EmptyText = State == BarState.Empty ? Loc.T("UsageBar_Empty") : "";
        CanCycle = eligible.Count >= 2;
        ShowCurve = Style == SideStripStyle.HeroStack && config.ShowSystemLoad && config.ShowLoadHistory;

        SyncList(_logos, focus == null
            ? []
            : eligible.Select(usage => LogoOf(usage, focus, config.AlertThreshold)).ToList());
        Load = loadItem;
        FeaturedGauge = BuildFeatured(focus, focusViews, loadItem);
        SyncList(_loadRings, SmallLoadRings(loadItem));
        _quotaColumns = BuildQuotaColumns(focus, focusViews, views);
        SyncList(_columns, loadItem == null ? _quotaColumns : [.. _quotaColumns, CpuColumn(loadItem), MemoryColumn(loadItem)]);
        HeroCard = focus == null ? null : BuildHeroCard(focus, focusViews);
        HeroRows = focus == null
            ? []
            : eligible
                .Where(usage => usage.ProviderId != focus.ProviderId)
                .Select(usage => OtherRowOf(usage, views))
                .ToList();
        Rings = _chosenGauge != null && FeaturedGauge != null
            ? new BarRings(FeaturedGauge.Ring, null, FeaturedGauge.Ring.Text,
                SmallQuotaRings(views, OtherRingCount, FeaturedGauge.Ring.Selection))
                { Legend = _chosenGauge.Kind is BarGaugeKind.Cpu or BarGaugeKind.Memory ? "" : FeaturedGauge.Ring.Label,
                    LegendTooltip = FeaturedGauge.Ring.Tooltip }
            : focus == null ? null : BuildRings(focus, focusViews, otherViews);
        _quotaDots = BuildQuotaDots(focus, views, FeaturedGauge?.Ring.Selection);
        Reservoir = State == BarState.Empty ? null : BuildReservoir();

        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(string.Empty));
    }

    /// <summary>
    /// L'IA mise en avant : la sélection du bandeau si elle est éligible, sinon la première éligible.
    /// </summary>
    private AiUsage? FindFocus(List<AiUsage> eligible) =>
        eligible.Where(usage => usage.ProviderId == _usage.SoloProviderId).FirstOrDefault()
        ?? eligible.FirstOrDefault();

    private static WindowView ViewOf(QuotaGauge quota, List<AiUsage> eligible)
    {
        var usage = eligible.Where(candidate => candidate.ProviderId == quota.ProviderId).First();
        var window = (quota.WindowKind == UsageWindowKind.Session ? usage.Session : usage.Week)
            ?? throw new InvalidOperationException("The planner returned a window the snapshot does not have.");
        return new WindowView(usage, quota, window);
    }

    // --- Éléments partagés par les styles

    /// <summary>Le gris d'un relevé ancien l'emporte sur la couleur d'alerte.</summary>
    private static string ColorOf(WindowView view) =>
        view.Gauge.IsStale ? StaleColor
        : view.Gauge.IsAlert ? UsageFormat.Critical
        : view.Usage.AccentColor;

    private static string ShortLabelOf(WindowView view) =>
        view.Window.Label.Length > 0
            ? view.Window.Label
            : Loc.T(view.Gauge.WindowKind == UsageWindowKind.Session ? "StreamDock_SessionShort" : "StreamDock_WeekShort");

    private static string FullLabelOf(WindowView view) =>
        view.Window.Label.Length > 0
            ? view.Window.Label
            : Loc.T(view.Gauge.WindowKind == UsageWindowKind.Session ? "Usage_Gauge_Session" : "Usage_Gauge_Week");

    private static string PercentOf(int percent) => Loc.F("UsageBar_Percent", percent);

    private static double FractionOf(WindowView view) => view.Window.UsedPct / 100d;

    /// <summary>
    /// Nom de la fenêtre, consommé et restant, remise à zéro si elle est connue, ancienneté d'un relevé ancien.
    /// </summary>
    private string TooltipOf(WindowView view)
    {
        var lines = new List<string> { Loc.F("UsageBar_Window_Tooltip", view.Usage.Name, FullLabelOf(view)) };

        lines.Add(view.Window.ObservedAt is { } observedAt
            ? Loc.F("UsageBar_Window_Stale", view.Window.UsedPct, view.Window.RemainingPct, UsageFormat.Age(observedAt, _clock()))
            : Loc.F("Usage_Gauge_Tooltip", view.Window.UsedPct, view.Window.RemainingPct));

        if (view.Window.ResetsAt != null)
            lines.Add(Loc.F("Usage_Reset_Tooltip", UsageFormat.Reset(view.Window.ResetsAt, _clock())));

        return string.Join("\n", lines);
    }

    /// <summary>
    /// Début de la notice, avant le premier « — » : « Quota Codex indisponible — réessai dans 5 min » donne
    /// « Quota Codex indisponible ». Le texte complet est dans l'infobulle.
    /// </summary>
    private static string NoticeTitleOf(AiUsage usage)
    {
        var separatorIndex = usage.QuotaNotice.IndexOf(NoticeTitleSeparator, StringComparison.Ordinal);
        var title = separatorIndex < 0 ? usage.QuotaNotice : usage.QuotaNotice[..separatorIndex];
        return Shorten(title.Trim(), NoticeTitleMax);
    }

    private static string NoticeTooltipOf(AiUsage usage)
    {
        return UsageHoverCard.NoticeTooltipOf(usage);
    }

    private UsageHoverCard HoverCardOf(AiUsage usage, UsageWindow? window = null) =>
        UsageHoverCard.Create(usage, window, _usage.Config.AlertThreshold, _clock());

    /// <summary>
    /// Met <paramref name="target"/> d'accord avec <paramref name="source"/> en ne touchant que ce qui diffère : un élément
    /// égal garde sa place, donc son conteneur dans la vue.
    /// </summary>
    private static void SyncList<T>(ObservableCollection<T> target, IReadOnlyList<T> source)
    {
        for (var index = 0; index < source.Count && index < target.Count; index++)
        {
            if (EqualityComparer<T>.Default.Equals(target[index], source[index]) == false)
                target[index] = source[index];
        }

        while (target.Count > source.Count)
            target.RemoveAt(target.Count - 1);
        for (var index = target.Count; index < source.Count; index++)
            target.Add(source[index]);
    }

    /// <summary>Texte réduit à <paramref name="max"/> caractères ; l'infobulle porte le texte complet.</summary>
    private static string Shorten(string text, int max) =>
        text.Length <= max ? text : text[..(max - 1)] + Ellipsis;

    // --- Tête de barre

    private static BarLogo LogoOf(AiUsage usage, AiUsage focus, int alertThreshold)
    {
        var windows = new[] { usage.Session, usage.Week }.OfType<UsageWindow>().ToList();
        var isSelected = usage.ProviderId == focus.ProviderId;
        var hasAlertDot = isSelected == false
                          && windows.Any(window => window.ObservedAt == null
                                                   && UsageFormat.IsCritical(window.UsedPct, alertThreshold));
        var tooltip = windows.Count > 0
            ? Loc.F("UsageBar_Logo_Tooltip", usage.Name, windows.Max(window => window.UsedPct))
            : Loc.F("UsageBar_Logo_Notice", usage.Name, usage.QuotaNotice);

        return new BarLogo(usage.ProviderId, usage.Glyph, usage.AccentColor, isSelected, hasAlertDot, tooltip,
            Loc.F("UsageBar_Logo_Accessible", usage.Name), usage.UsageUrl);
    }

    // --- Charge du PC

    private BarLoad BuildLoad()
    {
        var current = _feed.Current;
        var cpuText = LoadText(current.CpuPct);
        var memoryText = LoadText(current.MemoryPct);

        return new BarLoad(cpuText, memoryText,
            (current.CpuPct ?? 0) / 100d, (current.MemoryPct ?? 0) / 100d, CpuColor, MemoryColor,
            Loc.F("UsageBar_Load_Tooltip", cpuText, memoryText),
            _feed.History.Select(load => load.CpuPct).ToList(),
            _feed.History.Select(load => load.MemoryPct).ToList())
        {
            Legend = Loc.F("UsageBar_Legend", Loc.T("StreamDock_Cpu"), Loc.T("StreamDock_Memory")),
            CpuNumber = NumberText(current.CpuPct),
            MemoryNumber = NumberText(current.MemoryPct),
        };
    }

    private static string LoadText(int? percent) => percent == null ? UsageViewModel.Unknown : PercentOf(percent.Value);

    /// <summary>Le nombre seul, produit à partir de l'entier ; « — » quand la mesure manque.</summary>
    private static string NumberText(int? percent) =>
        percent == null ? UsageViewModel.Unknown : percent.Value.ToString(CultureInfo.CurrentCulture);

    private static BarColumn CpuColumn(BarLoad load) =>
        new(Loc.T("StreamDock_Cpu"), load.CpuText, load.CpuFraction, CpuColor, Loc.F("UsageBar_Cpu_Tooltip", load.CpuText))
        { NumberText = load.CpuNumber, NumberBrushKey = NeutralNumberKey };

    private static BarColumn MemoryColumn(BarLoad load) =>
        new(Loc.T("StreamDock_Memory"), load.MemoryText, load.MemoryFraction, MemoryColor,
            Loc.F("UsageBar_Memory_Tooltip", load.MemoryText))
        { NumberText = load.MemoryNumber, NumberBrushKey = NeutralNumberKey };

    private static BarRing CpuRing(BarLoad load) =>
        new(Loc.T("StreamDock_Cpu"), load.CpuText, load.CpuFraction, CpuColor, Loc.F("UsageBar_Cpu_Tooltip", load.CpuText))
        { NumberText = load.CpuNumber, NumberBrushKey = NeutralNumberKey, Selection = new BarGauge("", BarGaugeKind.Cpu) };

    private static BarRing MemoryRing(BarLoad load) =>
        new(Loc.T("StreamDock_Memory"), load.MemoryText, load.MemoryFraction, MemoryColor,
            Loc.F("UsageBar_Memory_Tooltip", load.MemoryText))
        { NumberText = load.MemoryNumber, NumberBrushKey = NeutralNumberKey, Selection = new BarGauge("", BarGaugeKind.Memory) };

    private static void UpdateLoadRing(BarRing ring, BarLoad load)
    {
        if (ring.Selection?.Kind == BarGaugeKind.Cpu)
            ring.UpdateLoad(load.CpuText, load.CpuFraction, Loc.F("UsageBar_Cpu_Tooltip", load.CpuText), load.CpuNumber);
        else if (ring.Selection?.Kind == BarGaugeKind.Memory)
            ring.UpdateLoad(load.MemoryText, load.MemoryFraction, Loc.F("UsageBar_Memory_Tooltip", load.MemoryText), load.MemoryNumber);
    }

    private IReadOnlyList<BarRing> SmallLoadRings(BarLoad? load) => load == null
        ? []
        : new[] { CpuRing(load), MemoryRing(load) }.Where(ring => ring.Selection != _chosenGauge).ToList();

    private static BarFeaturedGauge LoadFeatured(BarLoad load, BarGaugeKind kind)
    {
        var ring = kind == BarGaugeKind.Cpu ? CpuRing(load) : MemoryRing(load);
        return new BarFeaturedGauge(ring, "", ring.Label, "", "");
    }

    private BarFeaturedGauge? BuildFeatured(AiUsage? focus, List<WindowView> focusViews, BarLoad? load)
    {
        if (_chosenGauge?.Kind is BarGaugeKind.Cpu or BarGaugeKind.Memory && load != null)
            return LoadFeatured(load, _chosenGauge.Kind);

        var selected = focusViews.Where(view => SelectionOf(view) == _chosenGauge).FirstOrDefault();
        var top = selected ?? focusViews.FirstOrDefault();
        if (top != null)
            return new BarFeaturedGauge(RingOf(top), top.Usage.Glyph, Shorten(top.Usage.Name, CardNameMax),
                Shorten(ShortLabelOf(top), CardLabelMax), top.Usage.UsageUrl);
        if (focus == null) return null;

        var warning = new BarRing(NoticeTitleOf(focus), WarningGlyph, 0, UsageFormat.High,
            NoticeTooltipOf(focus), focus.ProviderId, IsWarning: true)
            { NumberText = WarningGlyph, HoverCard = HoverCardOf(focus) };
        return new BarFeaturedGauge(warning, focus.Glyph, Shorten(focus.Name, CardNameMax), "", focus.UsageUrl);
    }

    // --- Colonnes

    /// <summary>
    /// Trois fenêtres au plus, celles de l'IA mise en avant d'abord. Une IA mise en avant sans fenêtre garde sa place
    /// par une colonne d'avertissement qui porte le titre de sa notice.
    /// </summary>
    private IReadOnlyList<BarColumn> BuildQuotaColumns(AiUsage? focus, List<WindowView> focusViews, List<WindowView> views)
    {
        var columns = new List<BarColumn>();
        if (focus != null && focusViews.Count == 0)
        {
            columns.Add(new BarColumn(NoticeTitleOf(focus), WarningGlyph, 0, UsageFormat.High, NoticeTooltipOf(focus),
                focus.Glyph, IsWarning: true, ProviderId: focus.ProviderId)
                { NumberText = WarningGlyph, HoverCard = HoverCardOf(focus) });
        }

        columns.AddRange(views.Take(QuotaColumnCount - columns.Count).Select(view =>
            new BarColumn(Shorten(ShortLabelOf(view), ColumnLabelMax), PercentOf(view.Window.UsedPct), FractionOf(view),
                ColorOf(view), TooltipOf(view), view.Usage.Glyph, ProviderId: view.Usage.ProviderId)
            { NumberText = NumberText(view.Window.UsedPct), HoverCard = HoverCardOf(view.Usage, view.Window) }));
        return columns;
    }

    // --- Héros

    private BarHeroCard BuildHeroCard(AiUsage focus, List<WindowView> focusViews)
    {
        var name = Shorten(focus.Name, CardNameMax);
        if (focusViews.Count == 0)
        {
            return new BarHeroCard(focus.ProviderId, focus.Glyph, name, "", WarningGlyph, 0, UsageFormat.High, "",
                IsWarning: true, NoticeTitleOf(focus), NoticeTooltipOf(focus), SecondWindow: null)
                { HoverCard = HoverCardOf(focus) };
        }

        var top = focusViews[0];
        var second = focusViews.Count > 1 ? HeroRowOf(focusViews[1]) : null;
        return new BarHeroCard(focus.ProviderId, focus.Glyph, name, Shorten(ShortLabelOf(top), CardLabelMax),
            PercentOf(top.Window.UsedPct), FractionOf(top), ColorOf(top),
            UsageFormat.Reset(top.Window.ResetsAt, _clock()), IsWarning: false, "", TooltipOf(top), second)
        {
            ResetLabel = ResetLabelOf(top.Window),
            HoverCard = HoverCardOf(top.Usage, top.Window),
        };
    }

    private string ResetLabelOf(UsageWindow window)
    {
        var reset = UsageFormat.Reset(window.ResetsAt, _clock());
        return reset.Length == 0 ? "" : Loc.F("UsageBar_Reset", reset);
    }

    private BarHeroRow HeroRowOf(WindowView view) =>
        new(view.Usage.ProviderId, view.Usage.Glyph, Shorten(ShortLabelOf(view), RowLabelMax),
            PercentOf(view.Window.UsedPct), FractionOf(view), ColorOf(view), TooltipOf(view))
            { HoverCard = HoverCardOf(view.Usage, view.Window) };

    /// <summary>
    /// Un autre fournisseur : sa fenêtre la plus consommée, ou un avertissement s'il n'a que sa notice.
    /// </summary>
    private BarHeroRow OtherRowOf(AiUsage usage, List<WindowView> views)
    {
        var name = Shorten(usage.Name, RowLabelMax);
        var top = views.Where(view => view.Usage.ProviderId == usage.ProviderId).FirstOrDefault();
        if (top != null) return HeroRowOf(top) with { Label = name };

        return new BarHeroRow(usage.ProviderId, usage.Glyph, name, WarningGlyph, 0, UsageFormat.High,
            NoticeTooltipOf(usage), IsWarning: true) { HoverCard = HoverCardOf(usage) };
    }

    // --- Anneaux

    private BarRings BuildRings(AiUsage focus, List<WindowView> focusViews, List<WindowView> otherViews)
    {
        var others = otherViews.Take(OtherRingCount).Select(RingOf).ToList();
        if (focusViews.Count == 0)
        {
            var warning = new BarRing(NoticeTitleOf(focus), WarningGlyph, 0, UsageFormat.High, NoticeTooltipOf(focus), focus.ProviderId,
                IsWarning: true) { NumberText = WarningGlyph, HoverCard = HoverCardOf(focus) };
            return new BarRings(warning, null, WarningGlyph, others) { Legend = warning.Label, LegendTooltip = warning.Tooltip };
        }

        // Emplacement fixe par type : la session dehors, la semaine dedans, quelle que soit la plus consommée.
        var session = focusViews.Where(view => view.Gauge.WindowKind == UsageWindowKind.Session).FirstOrDefault();
        var week = focusViews.Where(view => view.Gauge.WindowKind == UsageWindowKind.Week).FirstOrDefault();
        var outer = session ?? week;
        var inner = session == null ? null : week;
        var highest = focusViews.Max(view => view.Window.UsedPct);

        var outerRing = outer == null ? null : RingOf(outer);
        var innerRing = inner == null ? null : RingOf(inner);
        var legend = innerRing == null
            ? outerRing?.Label ?? ""
            : Loc.F("UsageBar_Legend", outerRing?.Label ?? "", innerRing.Label);
        var legendTooltip = innerRing == null
            ? outerRing?.Tooltip ?? ""
            : outerRing?.Tooltip + Environment.NewLine + Environment.NewLine + innerRing.Tooltip;
        return new BarRings(outerRing, innerRing, PercentOf(highest), others) { Legend = legend, LegendTooltip = legendTooltip };
    }

    private BarRing RingOf(WindowView view) =>
        new(Shorten(ShortLabelOf(view), RingLabelMax), PercentOf(view.Window.UsedPct), FractionOf(view),
            ColorOf(view), TooltipOf(view), view.Usage.ProviderId)
            { NumberText = NumberText(view.Window.UsedPct), Selection = SelectionOf(view),
                HoverCard = HoverCardOf(view.Usage, view.Window) };

    private static BarGauge SelectionOf(WindowView view) => new(view.Usage.ProviderId,
        view.Gauge.WindowKind == UsageWindowKind.Session ? BarGaugeKind.Session : BarGaugeKind.Week);

    // --- Réservoir

    /// <summary>
    /// Fenêtres autres que la grande jauge, avec priorité à celle qui vient de redescendre.
    /// </summary>
    private List<BarRing> SmallQuotaRings(List<WindowView> views, int count, BarGauge? featuredSelection) => views.Select(RingOf)
        .Where(ring => ring.Selection != featuredSelection)
        .OrderByDescending(ring => ring.Selection == _previousGauge)
        .Take(count).ToList();

    private IReadOnlyList<BarRing> BuildQuotaDots(AiUsage? focus, List<WindowView> views, BarGauge? featuredSelection)
    {
        if (focus == null) return [];

        var dots = SmallQuotaRings(views, QuotaDotCount, featuredSelection);
        while (dots.Count < QuotaDotCount)
            dots.Add(new BarRing("", "", 0, StaleColor, "", IsEmpty: true));
        return dots;
    }

    /// <summary>
    /// Niveau de la jauge mise en grand, puis pastilles de quota et de charge restantes, sans dupliquer la grande jauge.
    /// </summary>
    private BarReservoir BuildReservoir()
    {
        SyncList(_dots, [.. _quotaDots, .. _loadRings]);
        IReadOnlyList<BarRing> dots = _dots;

        if (FeaturedGauge == null)
            return new BarReservoir(false, 0, "", "", StaleColor, "", dots);
        var ring = FeaturedGauge.Ring;
        return new BarReservoir(ring.IsWarning == false, ring.Fraction, ring.Text,
            ring.IsWarning ? ring.Label : "", ring.Color, ring.Tooltip, dots) { HoverCard = ring.HoverCard };
    }
}
