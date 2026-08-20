using System.Globalization;

namespace DockPad.Services.Usage;

/// <summary>
/// Estimation du coût d'une consommation Claude, à partir des tarifs publics par modèle.
/// </summary>
/// <remarks>
/// <para>
/// C'est une <b>estimation</b>, affichée sous le libellé « Coût est. » : un abonnement Max ou Pro
/// ne facture pas au jeton, et le mois en cours n'est pas une facture.
/// </para>
/// <para>
/// Tarifs en dollars par million de jetons, relevés le 2026-06-24. La devise est celle de la
/// source et n'est jamais convertie — convertir demanderait un taux de change, donc un taux figé
/// qui dérive ou un appel réseau, pour une valeur déjà approximative.
/// </para>
/// <para>
/// Les multiplicateurs de cache sont ceux de la documentation Anthropic : une écriture de cache
/// coûte 1,25 fois l'entrée (TTL 5 minutes, celui qu'utilise Claude Code), une lecture 0,1 fois.
/// </para>
/// </remarks>
public static class ClaudePricing
{
    /// <summary>Tarif d'un modèle, en dollars par million de jetons.</summary>
    private readonly record struct Rate(decimal Input, decimal Output);

    private const decimal CacheWriteMultiplier = 1.25m;
    private const decimal CacheReadMultiplier  = 0.10m;

    /// <summary>
    /// Correspondance par préfixe : les transcripts portent des identifiants datés
    /// (<c>claude-sonnet-4-6-20251114</c>). Ordre du plus spécifique au plus général — la première
    /// correspondance gagne.
    /// </summary>
    private static readonly (string Prefix, Rate Rate)[] Rates =
    [
        ("claude-fable-5",   new Rate(10m, 50m)),
        ("claude-mythos-5",  new Rate(10m, 50m)),
        ("claude-opus-5",    new Rate(5m,  25m)),
        ("claude-opus-4-8",  new Rate(5m,  25m)),
        ("claude-opus-4-7",  new Rate(5m,  25m)),
        ("claude-opus-4-6",  new Rate(5m,  25m)),
        ("claude-sonnet-5",  new Rate(3m,  15m)),
        ("claude-sonnet-4-6", new Rate(3m, 15m)),
        ("claude-haiku-4-5", new Rate(1m,   5m)),
    ];

    /// <summary>
    /// Tarif appliqué à un modèle absent de la table. Sonnet plutôt que zéro : un coût nul se lit
    /// comme « gratuit », alors qu'un modèle inconnu est le plus souvent une version antérieure ou
    /// postérieure dont le tarif est du même ordre. Les modèles retirés (Opus 4.1 et avant) sont
    /// donc sous-estimés — assumé, la colonne annonce une estimation.
    /// </summary>
    private static readonly Rate Fallback = new(3m, 15m);

    /// <summary>Coût estimé en dollars des jetons fournis.</summary>
    public static decimal Cost(string model, long input, long output, long cacheWrite, long cacheRead)
    {
        var rate = RateFor(model);
        return (input      * rate.Input
             +  output     * rate.Output
             +  cacheWrite * rate.Input * CacheWriteMultiplier
             +  cacheRead  * rate.Input * CacheReadMultiplier) / 1_000_000m;
    }

    /// <summary>Coût formaté en dollars, culture figée : « $3.80 ».</summary>
    public static string Format(decimal usd) =>
        usd.ToString("$0.00", CultureInfo.InvariantCulture);

    private static Rate RateFor(string model)
    {
        if (string.IsNullOrWhiteSpace(model)) return Fallback;
        foreach (var (prefix, rate) in Rates)
        {
            if (model.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) return rate;
        }
        return Fallback;
    }
}
