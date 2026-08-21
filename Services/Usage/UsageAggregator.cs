namespace DockPad.Services.Usage;

/// <summary>
/// Agrégation session / jour / mois d'une liste de consommations, commune à tous les fournisseurs.
/// </summary>
/// <remarks>
/// Extrait de <c>ClaudeUsageReader</c> quand les fournisseurs Gemini, Codex et Copilot sont arrivés :
/// chacun lit sa source à sa façon, mais tous comptent le temps de la même manière. Fonctions pures,
/// horloge en paramètre.
/// </remarks>
public static class UsageAggregator
{
    /// <summary>
    /// Durée du bloc de session, alignée sur la fenêtre de quota de Claude. Les autres fournisseurs
    /// n'ont pas cette notion, mais un « depuis 5 h » reste une mesure utile pour eux aussi.
    /// </summary>
    public static readonly TimeSpan BlockWindow = TimeSpan.FromHours(5);

    /// <summary>Une consommation normalisée, dédupliquée, en heure locale.</summary>
    /// <param name="Key">
    /// Clé de déduplication. Sa composition appartient au lecteur : le même message peut être
    /// réécrit dans plusieurs fichiers, et chaque outil a sa propre façon de l'identifier.
    /// </param>
    public sealed record UsageEntry(
        string Key, DateTime Timestamp, string Model,
        long Input, long Output, long CacheWrite, long CacheRead)
    {
        public long Total => Input + Output + CacheWrite + CacheRead;
    }

    /// <summary>Totaux prêts à afficher. <paramref name="Cost"/> porte sur le mois en cours.</summary>
    public sealed record UsageTotals(
        long Session, long Day, long Month, int Requests, string Model, decimal Cost);

    /// <summary>
    /// Totaux session / jour / mois.
    /// </summary>
    /// <param name="now">
    /// Explicite : sans lui la fonction dépendrait de l'horloge et les bornes ne seraient pas
    /// testables.
    /// </param>
    /// <param name="cost">
    /// Coût d'une entrée, ou <c>null</c> quand le fournisseur n'a pas de tarif public exploitable —
    /// le total reste alors à zéro et la colonne affiche un tiret. Inventer un tarif serait pire
    /// qu'avouer ne pas savoir.
    /// </param>
    public static UsageTotals Aggregate(IEnumerable<UsageEntry> entries, DateTime now,
                                        Func<UsageEntry, decimal>? cost = null)
    {
        var ordered = entries.OrderBy(e => e.Timestamp).ToList();
        if (ordered.Count == 0) return new UsageTotals(0, 0, 0, 0, "", 0m);

        var dayStart = now.Date;
        var monthStart = new DateTime(now.Year, now.Month, 1);

        long day = 0, month = 0;
        int requests = 0;
        decimal total = 0m;

        foreach (var e in ordered)
        {
            if (e.Timestamp >= monthStart)
            {
                month += e.Total;
                if (cost is not null) total += cost(e);
            }
            if (e.Timestamp >= dayStart)
            {
                day += e.Total;
                requests++;
            }
        }

        return new UsageTotals(
            Session: SessionTotal(ordered, now),
            Day: day,
            Month: month,
            Requests: requests,
            Model: ordered[^1].Model,
            Cost: total);
    }

    /// <summary>
    /// Jetons du bloc actif. Les blocs sont <b>ancrés</b> : un bloc démarre à la première activité
    /// qu'aucun bloc ne couvre et dure <see cref="BlockWindow"/>. Une coupure plus longue que la
    /// fenêtre ouvre un nouveau bloc. Seul celui qui contient <paramref name="now"/> est actif — si
    /// le dernier bloc s'est fermé avant, la session est à zéro plutôt qu'à un total périmé.
    /// </summary>
    private static long SessionTotal(List<UsageEntry> ordered, DateTime now)
    {
        var blockStart = ordered[0].Timestamp;
        long total = 0;

        foreach (var e in ordered)
        {
            if (e.Timestamp >= blockStart + BlockWindow)
            {
                blockStart = e.Timestamp;   // nouveau bloc
                total = 0;
            }
            total += e.Total;
        }

        return now < blockStart + BlockWindow ? total : 0;
    }
}
