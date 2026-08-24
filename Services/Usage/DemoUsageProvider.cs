using System.Threading;
using System.Threading.Tasks;
using DockPad.Models;

namespace DockPad.Services.Usage;

/// <summary>
/// Fournisseur de démonstration : un jeu de valeurs fixes, paramétrable.
/// </summary>
/// <remarks>
/// <para>
/// Trois usages : produire les captures des notes de version et de la documentation sans exposer de
/// consommation réelle, donner un second onglet pour exercer le changement de fournisseur dans
/// l'interface, et servir de référence à qui écrira le provider suivant.
/// </para>
/// <para>
/// Paramétrable et non figé : l'application en inscrit <b>une</b> instance (« Démo »), et
/// <c>tools/UsageShot</c> en instancie plusieurs pour reconstituer plusieurs assistants dans ses
/// captures. Deux classes de fausses données divergeraient à la première retouche.
/// </para>
/// </remarks>
public sealed class DemoUsageProvider : IUsageProvider
{
    /// <summary>
    /// Valeurs affichées. Les remises à zéro sont des <b>décalages</b> et non des dates : une
    /// capture doit rester reproductible, et une date absolue serait dans le passé dès le lendemain.
    /// </summary>
    public sealed record DemoValues(
        string Model,
        long Session, long Day, long Month, int Requests, string Cost,
        int SessionUsedPct, TimeSpan SessionResetIn,
        int WeekUsedPct, TimeSpan WeekResetIn,
        string UsageUrl = "");

    private readonly string _glyph;
    private readonly string _accent;
    private readonly DemoValues _values;
    private readonly Func<DateTime> _clock;

    public string Id { get; }
    public string Name { get; }

    public DemoUsageProvider(string id, string name, string glyph, string accent,
                             DemoValues values, Func<DateTime>? clock = null)
    {
        Id = id;
        Name = name;
        _glyph = glyph;
        _accent = accent;
        _values = values;
        _clock = clock ?? (() => DateTime.Now);
    }

    /// <summary>Le fournisseur de démonstration est toujours disponible, et toujours masqué au départ.</summary>
    public AiProbe Probe() => new()
    {
        Available = true,
        DisplayName = Name,
        Glyph = _glyph,
        AccentColor = _accent,
        DataPath = "",
        Detail = Loc.T("Probe_DemoData"),
        IsDemo = true,
        HiddenByDefault = true,
    };

    public Task<AiUsage?> ReadAsync(CancellationToken ct)
    {
        var now = _clock();
        return Task.FromResult<AiUsage?>(new AiUsage
        {
            ProviderId = Id,
            Name = Name,
            Glyph = _glyph,
            AccentColor = _accent,
            Model = _values.Model,
            Cost = _values.Cost,
            SessionTokens = _values.Session,
            DayTokens = _values.Day,
            MonthTokens = _values.Month,
            Requests = _values.Requests,
            UsageUrl = _values.UsageUrl,
            Session = new UsageWindow { UsedPct = _values.SessionUsedPct, ResetsAt = now + _values.SessionResetIn },
            Week = new UsageWindow { UsedPct = _values.WeekUsedPct, ResetsAt = now + _values.WeekResetIn },
            IsDemo = true,
        });
    }

    /// <summary>
    /// L'instance inscrite dans l'application. Coûts en dollars : la devise est celle de la source
    /// et n'est jamais convertie, y compris pour des valeurs inventées.
    /// </summary>
    public static DemoUsageProvider Default() => new(
        id: "demo", name: "Démo", glyph: "D", accent: "#7C3AED",
        values: new DemoValues(
            Model: "claude-sonnet-5",
            Session: 12_400, Day: 86_000, Month: 1_200_000, Requests: 47, Cost: "$4",
            SessionUsedPct: 62, SessionResetIn: TimeSpan.FromHours(2) + TimeSpan.FromMinutes(40),
            WeekUsedPct: 44, WeekResetIn: TimeSpan.FromDays(4)));
}
