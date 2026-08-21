using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Threading;
using DockPad.Models;

namespace DockPad.Services.Usage;

/// <summary>
/// État affichable du bandeau Usage IA : onglets, jauges, métriques. Le XAML ne calcule rien —
/// tout ce qui se décide se décide ici, et se teste sans WPF.
/// </summary>
public sealed class UsageViewModel : INotifyPropertyChanged
{
    /// <summary>Symbole d'une valeur inconnue. Jamais « 0 », qui se lirait comme une mesure.</summary>
    public const string Unknown = "—";

    private static readonly TimeSpan RefreshInterval = TimeSpan.FromMinutes(1);

    private readonly UsageService _service;
    private readonly Func<UsageConfig> _loadConfig;
    private readonly Func<DateTime> _clock;

    private DispatcherTimer? _timer;

    /// <summary>La fenêtre hôte est affichée : le bandeau doit se tenir à jour.</summary>
    private bool _running;
    private CancellationTokenSource? _inFlight;

    private UsageConfig _config = new();
    private bool _isLoading;
    private List<AiUsage> _snapshots = [];
    private string _selectedId = "";

    public UsageViewModel(UsageService? service = null,
                          Func<UsageConfig>? loadConfig = null,
                          Func<DateTime>? clock = null)
    {
        _service = service ?? new UsageService();
        // LoadForStartup et non Load : au premier lancement la config n'a aucun fournisseur, et
        // sans détection à froid le provider de démonstration serait visible d'emblée.
        _loadConfig = loadConfig ?? AiDetectionService.LoadForStartup;
        _clock = clock ?? (() => DateTime.Now);
    }

    public ObservableCollection<UsageTabItem> Tabs { get; } = [];
    public ObservableCollection<UsageMetric> Metrics { get; } = [];

    /// <summary>
    /// Le bandeau est-il affichable ? Faux si le réglage le désactive, si aucun fournisseur n'est
    /// visible, ou si aucun n'a de données — mieux vaut disparaître qu'afficher une rangée de zéros.
    /// </summary>
    public bool IsVisible { get; private set; }

    /// <summary>
    /// Faux quand il n'y a qu'un fournisseur visible : un onglet unique et cliquable suggère un
    /// choix qui n'existe pas. Son nom s'affiche alors en libellé statique.
    /// </summary>
    public bool ShowTabs { get; private set; }

    public string SoloName { get; private set; } = "";
    public string SoloGlyph { get; private set; } = "";
    public string SoloAccent { get; private set; } = "#000000";

    /// <summary>Le fournisseur affiché produit des données de démonstration.</summary>
    public bool IsDemo { get; private set; }

    /// <summary>
    /// Une lecture est en cours. Le bandeau garde les valeurs précédentes pendant ce temps — un
    /// rafraîchissement prend le temps de parcourir les transcripts, et l'attente doit se voir.
    /// </summary>
    public bool IsLoading
    {
        get => _isLoading;
        private set
        {
            if (_isLoading == value) return;
            _isLoading = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsLoading)));
            // Les onglets existants sont mis à jour en place : ils ne sont reconstruits qu'à la fin
            // du rafraîchissement, trop tard pour signaler l'attente.
            foreach (var tab in Tabs) tab.IsLoading = value && tab.IsSelected;
        }
    }

    /// <summary>Page web de consommation du fournisseur affiché, vide s'il n'en a pas.</summary>
    public string UsageUrl { get; private set; } = "";

    /// <summary>Le fournisseur affiché a une page web à ouvrir.</summary>
    public bool HasUsageUrl => UsageUrl.Length > 0;

    public UsageGaugeItem? SessionGauge { get; private set; }
    public UsageGaugeItem? WeekGauge { get; private set; }

    /// <summary>Démarre le rafraîchissement périodique. À appeler quand la fenêtre s'affiche.</summary>
    public void Start()
    {
        _running = true;
        _timer ??= CreateTimer();
        _timer.Start();
        _ = RefreshAsync();
    }

    /// <summary>
    /// Arrête le rafraîchissement. À appeler quand la fenêtre se masque : DockPad passe l'essentiel
    /// de son temps dans la barre système, et interroger dans le vide coûte du disque et du réseau.
    /// </summary>
    public void Stop()
    {
        _running = false;
        _timer?.Stop();
        Cancel();
        // Plus personne ne lit : le sablier n'a plus rien à annoncer. Sans ça il resterait allumé
        // jusqu'au prochain affichage, une lecture annulée ne repassant pas par la fin normale.
        IsLoading = false;
    }

    /// <summary>Change le fournisseur affiché. Sélection de session : n'écrit rien dans la config.</summary>
    public void Select(string providerId)
    {
        if (_snapshots.All(s => s.ProviderId != providerId)) return;
        _selectedId = providerId;
        Rebuild();
    }

    /// <summary>
    /// Relit les fournisseurs et republie l'état affichable.
    /// </summary>
    /// <remarks>
    /// <b>Une invocation dépassée ne publie rien.</b> Elle ne remet pas le sablier à zéro et
    /// n'écrase pas les instantanés : la plus récente est seule à tenir l'état. Sans cette garde,
    /// deux défauts se produisaient dès que deux rafraîchissements se suivaient de près — ce que
    /// fait exactement la séquence masquer puis réafficher : le <c>finally</c> de l'ancienne
    /// éteignait le sablier pendant la lecture de la nouvelle, et son chemin d'exception vidait le
    /// bandeau <i>après</i> que la nouvelle l'avait rempli.
    /// </remarks>
    public async Task RefreshAsync()
    {
        var cts = new CancellationTokenSource();
        Supersede(cts);
        IsLoading = true;

        var config = _config;
        List<AiUsage> snapshots;

        try
        {
            config = _loadConfig();
            SyncTimer(config);

            snapshots = config.Enabled
                ? await _service.RefreshAsync(config, cts.Token).ConfigureAwait(true)
                : [];
        }
        catch (OperationCanceledException)
        {
            return;   // une invocation plus récente a pris la main : elle tient l'état
        }
        catch (Exception ex)
        {
            LogService.Warn(ex, "Rafraîchissement du bandeau Usage IA");
            snapshots = [];
        }

        if (!ReferenceEquals(_inFlight, cts)) return;

        _config = config;
        _snapshots = snapshots;
        IsLoading = false;
        Rebuild();
    }

    /// <summary>
    /// Bandeau désactivé : on arrête aussi le timer. Sans ça il continuait de battre toutes les
    /// minutes pour relire la config et constater qu'il n'y a rien à faire — aucun fournisseur
    /// n'était interrogé, mais « désactivé » doit vouloir dire au repos.
    /// </summary>
    private void SyncTimer(UsageConfig config)
    {
        if (!_running) return;
        if (config.Enabled) _timer?.Start();
        else _timer?.Stop();
    }

    private DispatcherTimer CreateTimer()
    {
        var timer = new DispatcherTimer { Interval = RefreshInterval };
        timer.Tick += (_, _) => _ = RefreshAsync();
        return timer;
    }

    /// <summary>Annule la lecture en cours, s'il y en a une, sans en désigner de nouvelle.</summary>
    private void Cancel() => Supersede(null);

    /// <summary>
    /// Annule la lecture en cours et désigne <paramref name="next"/> comme la seule qui compte.
    /// </summary>
    /// <remarks>
    /// La source annulée n'est pas libérée : son jeton peut encore être détenu par une requête HTTP
    /// en vol, et libérer la source pendant qu'une inscription vit dessus lève une
    /// <c>ObjectDisposedException</c> — attrapée plus haut comme une panne de lecture, donc un
    /// bandeau vidé pour une raison inventée. Le ramasse-miettes s'en occupe.
    /// </remarks>
    private void Supersede(CancellationTokenSource? next)
    {
        var previous = _inFlight;
        _inFlight = next;
        previous?.Cancel();
    }

    private void Rebuild()
    {
        var selected = Resolve();

        IsVisible = _config.Enabled && selected is not null;
        ShowTabs = _snapshots.Count > 1;
        IsDemo = selected?.IsDemo ?? false;
        UsageUrl = selected?.UsageUrl ?? "";

        SoloName = selected?.Name ?? "";
        SoloGlyph = selected?.Glyph ?? "";
        SoloAccent = selected?.AccentColor ?? "#000000";

        BuildTabs(selected);
        BuildGauges(selected);
        BuildMetrics(selected);

        NotifyAll();
    }

    /// <summary>
    /// Le fournisseur à afficher : la sélection de session, sinon le fournisseur par défaut, sinon
    /// le premier visible. Un réglage qui pointe un fournisseur masqué ou inconnu ne bloque pas
    /// l'affichage et n'est pas effacé — le réafficher suffit à le rétablir.
    /// </summary>
    private AiUsage? Resolve()
    {
        if (_snapshots.Count == 0) return null;

        return _snapshots.FirstOrDefault(s => s.ProviderId == _selectedId)
            ?? _snapshots.FirstOrDefault(s => s.ProviderId == _config.DefaultProviderId)
            ?? _snapshots[0];
    }

    private void BuildTabs(AiUsage? selected)
    {
        Tabs.Clear();
        if (!ShowTabs) return;

        foreach (var snapshot in _snapshots)
        {
            Tabs.Add(new UsageTabItem
            {
                ProviderId = snapshot.ProviderId,
                Name = snapshot.Name,
                Glyph = snapshot.Glyph,
                Accent = snapshot.AccentColor,
                IsDemo = snapshot.IsDemo,
                IsSelected = snapshot.ProviderId == selected?.ProviderId,
                IsLoading = IsLoading && snapshot.ProviderId == selected?.ProviderId,
            });
        }
    }

    private void BuildGauges(AiUsage? selected)
    {
        SessionGauge = Gauge("session", selected?.Session);
        WeekGauge = Gauge("semaine", selected?.Week);
    }

    private UsageGaugeItem Gauge(string label, UsageWindow? window)
    {
        if (window is null)
        {
            return new UsageGaugeItem { Label = label, HasQuota = false, Color = UsageFormat.Ok };
        }

        return new UsageGaugeItem
        {
            Label = label,
            HasQuota = true,
            UsedPct = window.UsedPct,
            RemainingPct = window.RemainingPct,
            Reset = UsageFormat.Reset(window.ResetsAt, _clock()),
            Color = UsageFormat.GaugeColor(window.UsedPct, _config.AlertThreshold),
            // Le libellé est court par choix, mais « 62 % session » ne dit pas si le chiffre est le
            // consommé ou le restant. L'infobulle lève le doute sans coûter de place.
            Tooltip = $"{window.UsedPct} % utilisés, {window.RemainingPct} % restants",
        };
    }

    private void BuildMetrics(AiUsage? selected)
    {
        Metrics.Clear();
        if (selected is null) return;

        Metrics.Add(new UsageMetric { Label = "Session", Value = Tokens(selected.SessionTokens) });
        Metrics.Add(new UsageMetric { Label = "Jour", Value = UsageFormat.Tokens(selected.DayTokens) });
        Metrics.Add(new UsageMetric { Label = "Mois", Value = UsageFormat.Tokens(selected.MonthTokens) });
        Metrics.Add(new UsageMetric { Label = "Requêtes", Value = selected.Requests.ToString() });

        if (_config.ShowCost)
        {
            // La précision vient du fournisseur : le montant peut être élevé (l'équivalent API d'un
            // mois de travail intensif se compte en milliers) et se lit alors comme une facture,
            // mais la façon de facturer est propre à chaque source.
            Metrics.Add(new UsageMetric
            {
                Label = "Coût est.",
                Value = Text(selected.Cost),
                Tooltip = selected.CostNote,
            });
        }

        Metrics.Add(new UsageMetric { Label = "Modèle", Value = Text(selected.Model) });
    }

    /// <summary>
    /// Zéro jeton de session signifie « aucun bloc actif », pas « rien consommé » : c'est une
    /// absence de mesure, pas une mesure nulle.
    /// </summary>
    private static string Tokens(long value) => value == 0 ? Unknown : UsageFormat.Tokens(value);

    private static string Text(string value) => value.Length == 0 ? Unknown : value;

    public event PropertyChangedEventHandler? PropertyChanged;

    private void NotifyAll()
    {
        foreach (var name in new[]
                 {
                     nameof(IsVisible), nameof(ShowTabs), nameof(SoloName), nameof(SoloGlyph),
                     nameof(SoloAccent), nameof(IsDemo), nameof(SessionGauge), nameof(WeekGauge),
                     nameof(UsageUrl), nameof(HasUsageUrl),
                 })
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        }
    }
}
