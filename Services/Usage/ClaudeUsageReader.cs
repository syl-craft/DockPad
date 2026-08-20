using System.Globalization;
using System.IO;
using System.Text.Json;

namespace DockPad.Services.Usage;

/// <summary>
/// Lit les journaux locaux de Claude Code et en tire les totaux de consommation.
/// </summary>
/// <remarks>
/// <para>
/// Source : <c>~/.claude/projects/**/*.jsonl</c>, lignes <c>type:"assistant"</c> — quatre compteurs
/// dans <c>message.usage</c>, plus <c>message.model</c>, <c>message.id</c>, <c>requestId</c> et
/// <c>timestamp</c> (UTC, suffixé Z).
/// </para>
/// <para>
/// Aucun réseau, aucune dépendance : la classe est pure vis-à-vis de l'environnement — le dossier de
/// départ et l'horloge sont des paramètres, ce qui la rend testable sur un dossier temporaire.
/// </para>
/// </remarks>
public static class ClaudeUsageReader
{
    /// <summary>Durée du bloc de session, alignée sur la fenêtre de quota de Claude.</summary>
    public static readonly TimeSpan BlockWindow = TimeSpan.FromHours(5);

    /// <summary>Une consommation normalisée, dédupliquée, en heure locale.</summary>
    /// <param name="Key">Clé de déduplication : <c>message.id</c> + <c>requestId</c>.</param>
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
    /// Emplacements où Claude Code écrit ses transcripts. <b>Seule</b> fonction qui sait où chercher :
    /// le scan, la détection et les tests l'appellent tous. Deux listes de littéraux séparées, et
    /// c'est l'une des deux qui pourrit sans qu'un test le voie.
    /// </summary>
    public static IReadOnlyList<string> ScanRoots(string home) =>
    [
        Path.Combine(home, ".claude", "projects"),
        Path.Combine(home, ".config", "claude", "projects"),   // installation de style XDG
    ];

    /// <summary>
    /// Toutes les consommations postérieures à <paramref name="since"/> (heure locale), dédupliquées.
    /// </summary>
    /// <remarks>
    /// Les fichiers modifiés avant <paramref name="since"/> ne sont pas ouverts : ils ne peuvent pas
    /// contenir d'entrée dans la fenêtre. Avec plusieurs centaines de transcripts, c'est la
    /// différence entre un rafraîchissement instantané et une seconde de disque à chaque tick.
    /// </remarks>
    public static List<UsageEntry> Read(string home, DateTime since)
    {
        var entries = new List<UsageEntry>();
        var seen = new HashSet<string>(StringComparer.Ordinal);

        foreach (var root in ScanRoots(home))
        {
            if (!Directory.Exists(root)) continue;

            IEnumerable<string> files;
            try { files = Directory.EnumerateFiles(root, "*.jsonl", SearchOption.AllDirectories); }
            catch (Exception ex) { LogService.Warn(ex, $"Énumération des transcripts Claude ({root})"); continue; }

            foreach (var file in files)
            {
                try
                {
                    if (File.GetLastWriteTime(file) < since) continue;
                    ReadFile(file, since, entries, seen);
                }
                catch (Exception ex)
                {
                    LogService.Warn(ex, $"Lecture du transcript {Path.GetFileName(file)}");
                }
            }
        }

        return entries;
    }

    private static void ReadFile(string file, DateTime since, List<UsageEntry> entries, HashSet<string> seen)
    {
        // FileShare.ReadWrite : Claude Code garde le fichier ouvert en écriture pendant la session.
        using var stream = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        using var reader = new StreamReader(stream);

        while (reader.ReadLine() is { } line)
        {
            if (line.Length == 0) continue;
            var entry = ParseLine(line);
            if (entry is null) continue;
            if (entry.Timestamp < since) continue;
            if (!seen.Add(entry.Key)) continue;   // même message réécrit ailleurs
            entries.Add(entry);
        }
    }

    /// <summary>
    /// Une ligne de transcript, ou <c>null</c> si ce n'est pas une consommation exploitable.
    /// Tolérant par construction : la dernière ligne d'un fichier en cours d'écriture est un JSON
    /// tronqué, et ce n'est pas une anomalie à signaler.
    /// </summary>
    private static UsageEntry? ParseLine(string line)
    {
        try
        {
            using var doc = JsonDocument.Parse(line);
            var root = doc.RootElement;

            if (!root.TryGetProperty("type", out var type) || type.GetString() != "assistant") return null;
            if (!root.TryGetProperty("message", out var message)) return null;
            if (!message.TryGetProperty("usage", out var usage)) return null;

            var messageId = message.TryGetProperty("id", out var id) ? id.GetString() ?? "" : "";
            var requestId = root.TryGetProperty("requestId", out var rid) ? rid.GetString() ?? "" : "";
            if (messageId.Length == 0 && requestId.Length == 0) return null;

            if (!root.TryGetProperty("timestamp", out var ts)) return null;
            var raw = ts.GetString();
            if (!DateTimeOffset.TryParse(raw, CultureInfo.InvariantCulture,
                                         DateTimeStyles.AdjustToUniversal, out var utc)) return null;

            return new UsageEntry(
                Key: messageId + "|" + requestId,
                // Les timestamps sont en UTC, les bornes jour/mois sont locales : convertir ici, une
                // fois, plutôt que dans chaque agrégat. `LocalDateTime` et non `ToLocalTime().DateTime`
                // — le second rend un Kind « Unspecified », qui laisse un mélange UTC/local passer
                // inaperçu à la comparaison suivante.
                Timestamp: utc.LocalDateTime,
                Model: message.TryGetProperty("model", out var m) ? m.GetString() ?? "" : "",
                Input: Number(usage, "input_tokens"),
                Output: Number(usage, "output_tokens"),
                CacheWrite: Number(usage, "cache_creation_input_tokens"),
                CacheRead: Number(usage, "cache_read_input_tokens"));
        }
        catch (JsonException)
        {
            return null;   // ligne tronquée : Claude Code écrit pendant qu'on lit
        }
    }

    private static long Number(JsonElement parent, string name) =>
        parent.TryGetProperty(name, out var value) && value.TryGetInt64(out var n) ? n : 0;

    /// <summary>
    /// Totaux session / jour / mois. <paramref name="now"/> est explicite : sans lui, la fonction
    /// dépendrait de l'horloge et les bornes ne seraient pas testables.
    /// </summary>
    public static UsageTotals Aggregate(IEnumerable<UsageEntry> entries, DateTime now)
    {
        var ordered = entries.OrderBy(e => e.Timestamp).ToList();
        if (ordered.Count == 0) return new UsageTotals(0, 0, 0, 0, "", 0m);

        var dayStart = now.Date;
        var monthStart = new DateTime(now.Year, now.Month, 1);

        long day = 0, month = 0;
        int requests = 0;
        decimal cost = 0m;

        foreach (var e in ordered)
        {
            if (e.Timestamp >= monthStart)
            {
                month += e.Total;
                cost += ClaudePricing.Cost(e.Model, e.Input, e.Output, e.CacheWrite, e.CacheRead);
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
            Cost: cost);
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
