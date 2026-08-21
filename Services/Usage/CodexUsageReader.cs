using System.Globalization;
using System.IO;
using System.Text.Json;

namespace DockPad.Services.Usage;

/// <summary>
/// Lit les sessions locales de Codex.
/// </summary>
/// <remarks>
/// <para>
/// Source : <c>rollout-*.jsonl</c> sous <c>~/.codex/sessions</c> et <c>~/.codex/archived_sessions</c>.
/// Les lignes utiles portent <c>type:"event_msg"</c> avec <c>payload.type:"token_count"</c> ; le
/// delta du tour est dans <c>payload.info.last_token_usage</c>.
/// </para>
/// <para>
/// <b>Les deux racines doivent être lues.</b> Codex déplace un rollout de <c>sessions</c> vers
/// <c>archived_sessions</c> : ce n'est pas une consommation différente mais le même fichier qui
/// bouge. N'en lire qu'une ferait « disparaître » de la consommation passée.
/// </para>
/// <para>
/// <b>Limite connue : les sessions dérivées peuvent être comptées deux fois.</b> Un fork rejoue
/// l'historique du parent dans un nouveau rollout, qui réémet donc ses événements
/// <c>token_count</c>. Contrairement à Claude, ces événements ne portent pas d'identifiant de
/// message : il n'y a rien à dédupliquer entre fichiers. L'implémentation de référence
/// (<c>chattymin/PokeTokenBar</c>) résout le cas en comparant les rollouts entre eux, avec une
/// machinerie de plusieurs centaines de lignes — hors de proportion ici tant que personne n'a
/// constaté l'écart.
/// </para>
/// </remarks>
public static class CodexUsageReader
{
    /// <summary>Variable d'environnement qui déplace le dossier de Codex, comme le fait sa CLI.</summary>
    public const string HomeVariable = "CODEX_HOME";

    /// <summary>
    /// Racines de scan. <b>Seule</b> fonction qui sait où chercher : le scan, la détection et les
    /// tests l'appellent tous.
    /// </summary>
    public static IReadOnlyList<string> ScanRoots(string home)
    {
        var root = Environment.GetEnvironmentVariable(HomeVariable);
        var codex = string.IsNullOrWhiteSpace(root) ? Path.Combine(home, ".codex") : root.Trim().Trim('"');

        return
        [
            Path.Combine(codex, "sessions"),
            Path.Combine(codex, "archived_sessions"),
        ];
    }

    /// <summary>Toutes les consommations postérieures à <paramref name="since"/> (heure locale).</summary>
    public static List<UsageAggregator.UsageEntry> Read(string home, DateTime since)
    {
        var entries = new List<UsageAggregator.UsageEntry>();

        foreach (var root in ScanRoots(home).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if (!Directory.Exists(root)) continue;

            IEnumerable<string> files;
            try { files = Directory.EnumerateFiles(root, "rollout-*.jsonl", SearchOption.AllDirectories); }
            catch (Exception ex) { LogService.Warn(ex, $"Énumération des sessions Codex ({root})"); continue; }

            foreach (var file in files)
            {
                try
                {
                    if (File.GetLastWriteTime(file) < since) continue;
                    ReadFile(file, since, entries);
                }
                catch (Exception ex)
                {
                    LogService.Warn(ex, $"Lecture de la session Codex {Path.GetFileName(file)}");
                }
            }
        }

        return entries;
    }

    private static void ReadFile(string file, DateTime since, List<UsageAggregator.UsageEntry> entries)
    {
        using var stream = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        using var reader = new StreamReader(stream);

        var name = Path.GetFileNameWithoutExtension(file);
        var index = 0;

        while (reader.ReadLine() is { } line)
        {
            index++;
            if (line.Length == 0) continue;

            // Filtre à bas prix avant de payer l'analyse JSON : l'essentiel d'un gros rollout est
            // fait de lignes de conversation, qui ne contiennent pas ce marqueur.
            if (!line.Contains("token_count", StringComparison.Ordinal)) continue;

            var entry = ParseLine(line, name, index);
            if (entry is null || entry.Timestamp < since) continue;
            entries.Add(entry);
        }
    }

    /// <summary>
    /// Une ligne de rollout, ou <c>null</c> si ce n'est pas un relevé de jetons exploitable.
    /// </summary>
    /// <remarks>
    /// Correspondance des compteurs : <c>input_tokens</c> est le prompt entier et
    /// <c>cached_input_tokens</c> en est un sous-ensemble — on le soustrait pour ne pas compter deux
    /// fois. <c>output_tokens</c> inclut déjà le raisonnement, on n'ajoute donc pas
    /// <c>reasoning_output_tokens</c>.
    /// </remarks>
    private static UsageAggregator.UsageEntry? ParseLine(string line, string file, int index)
    {
        try
        {
            using var doc = JsonDocument.Parse(line);
            var root = doc.RootElement;

            if (!root.TryGetProperty("payload", out var payload)
                || payload.ValueKind != JsonValueKind.Object) return null;
            if (!payload.TryGetProperty("type", out var kind) || kind.GetString() != "token_count") return null;
            if (!payload.TryGetProperty("info", out var info)
                || !info.TryGetProperty("last_token_usage", out var usage)
                || usage.ValueKind != JsonValueKind.Object) return null;

            if (!root.TryGetProperty("timestamp", out var ts)
                || !DateTimeOffset.TryParse(ts.GetString(), CultureInfo.InvariantCulture,
                                            DateTimeStyles.AdjustToUniversal, out var utc)) return null;

            var input = Number(usage, "input_tokens");
            var cached = Number(usage, "cached_input_tokens");

            return new UsageAggregator.UsageEntry(
                // Pas d'identifiant de message dans ces événements : la clé est la position dans le
                // fichier. Elle suffit à ne pas compter deux fois une même ligne, pas à reconnaître
                // un tour rejoué dans un fork.
                Key: $"codex|{file}|{index}",
                Timestamp: utc.LocalDateTime,
                Model: info.TryGetProperty("model", out var m) ? m.GetString() ?? "" : "",
                Input: Math.Max(0, input - cached),
                Output: Number(usage, "output_tokens"),
                CacheWrite: Number(usage, "cache_write_input_tokens"),
                CacheRead: cached);
        }
        catch (JsonException)
        {
            return null;   // ligne tronquée : Codex écrit pendant qu'on lit
        }
    }

    private static long Number(JsonElement parent, string name) =>
        parent.TryGetProperty(name, out var value) && value.TryGetInt64(out var n) ? n : 0;
}
