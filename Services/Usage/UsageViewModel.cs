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
    private CancellationTokenSource? _inFlight;

    private UsageConfig _config = new();
    private List<AiUsage> _snapshots = [];
    private string _selectedId = "";

    public UsageViewModel(UsageService? service = null,
                          Func<UsageConfig>? loadConfig = null,
                          Func<DateTime>? clock = null)
    {
        _service = service ?? new UsageService();
        _loadConfig = loadConfig ?? UsageConfigService.Load;
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

    public UsageGaugeItem? SessionGauge { get; private set; }
    public UsageGaugeItem? WeekGauge { get; private set; }

    /// <summary>Démarre le rafraîchissement périodique. À appeler quand la fenêtre s'affiche.</summary>
    public void Start()
    {
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
        _timer?.Stop();
        Cancel();
    }

    /// <summary>Change le fournisseur affiché. Sélection de session : n'écrit rien dans la config.</summary>
    public void Select(string providerId)
    {
        if (_snapshots.All(s => s.ProviderId != providerId)) return;
        _selectedId = providerId;
        Rebuild();
    }

    public async Task RefreshAsync()
    {
        Cancel();
        var cts = new CancellationTokenSource();
        _inFlight = cts;

        try
        {
            _config = _loadConfig();
            _snapshots = _config.Enabled
                ? await _service.RefreshAsync(_config, cts.Token).ConfigureAwait(true)
                : [];
        }
        catch (OperationCanceledException)
        {
            return;   // un rafraîchissement plus récent a pris la main
        }
        catch (Exception ex)
        {
            LogService.Warn(ex, "Rafraîchissement du bandeau Usage IA");
            _snapshots = [];
        }

        Rebuild();
    }

    private DispatcherTimer CreateTimer()
    {
        var timer = new DispatcherTimer { Interval = RefreshInterval };
        timer.Tick += (_, _) => _ = RefreshAsync();
        return timer;
    }

    private void Cancel()
    {
        var previous = _inFlight;
        _inFlight = null;
        if (previous is null) return;
        previous.Cancel();
        previous.Dispose();
    }

    private void Rebuild()
    {
        var selected = Resolve();

        IsVisible = _config.Enabled && selected is not null;
        ShowTabs = _snapshots.Count > 1;
        IsDemo = selected?.IsDemo ?? false;

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
            Metrics.Add(new UsageMetric { Label = "Coût est.", Value = Text(selected.Cost) });
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
                 })
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        }
    }
}
