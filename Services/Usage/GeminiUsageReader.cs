using System.Globalization;
using System.IO;
using System.Text.Json;

namespace DockPad.Services.Usage;

/// <summary>
/// Lit les sessions locales de Gemini CLI.
/// </summary>
/// <remarks>
/// <para>
/// Source : <c>~/.gemini/tmp/&lt;hash&gt;/chats/session-*.json</c> — un document par session, avec un
/// tableau <c>messages</c> dont les réponses du modèle portent un objet <c>tokens</c>
/// (<c>input</c>, <c>cached</c>, <c>output</c>, <c>thoughts</c>, <c>tool</c>, <c>total</c>). La
/// variante <c>.jsonl</c> existe aussi, un objet par ligne.
/// </para>
/// <para>
/// <b>Seul <c>chats/</c> est scanné.</b> Le voisin <c>logs/</c> contient des <c>.jsonl</c> de trace
/// console et réseau, sans aucune consommation — mesuré 6 Mo sur cette machine. Les ouvrir coûterait
/// le prix d'un gros fichier pour zéro entrée.
/// </para>
/// </remarks>
public static class GeminiUsageReader
{
    /// <summary>
    /// Racine des sessions. <b>Seule</b> fonction qui sait où chercher : le scan, la détection et
    /// les tests l'appellent tous.
    /// </summary>
    public static string ScanRoot(string home) => Path.Combine(home, ".gemini", "tmp");

    /// <summary>Toutes les consommations postérieures à <paramref name="since"/> (heure locale).</summary>
    public static List<UsageAggregator.UsageEntry> Read(string home, DateTime since)
    {
        var entries = new List<UsageAggregator.UsageEntry>();
        var root = ScanRoot(home);
        if (!Directory.Exists(root)) return entries;

        IEnumerable<string> files;
        try
        {
            files = Directory.EnumerateFiles(root, "session-*.*", SearchOption.AllDirectories)
                .Where(IsChatSession);
        }
        catch (Exception ex)
        {
            LogService.Warn(ex, $"Énumération des sessions Gemini ({root})");
            return entries;
        }

        foreach (var file in files)
        {
            try
            {
                if (File.GetLastWriteTime(file) < since) continue;
                entries.AddRange(ReadFile(file, since));
            }
            catch (Exception ex)
            {
                LogService.Warn(ex, $"Lecture de la session Gemini {Path.GetFileName(file)}");
            }
        }

        return entries;
    }

    /// <summary>Fichier de conversation, et non de trace : le dossier parent doit être <c>chats</c>.</summary>
    private static bool IsChatSession(string path)
    {
        var extension = Path.GetExtension(path);
        if (extension is not (".json" or ".jsonl")) return false;
        return string.Equals(Path.GetFileName(Path.GetDirectoryName(path)), "chats",
                             StringComparison.OrdinalIgnoreCase);
    }

    private static List<UsageAggregator.UsageEntry> ReadFile(string file, DateTime since)
    {
        // Un message peut être réécrit plus loin (mise à jour en cours de réponse) : la dernière
        // valeur pour un id donné est la bonne, d'où le dictionnaire plutôt qu'une simple liste.
        var byId = new Dictionary<string, UsageAggregator.UsageEntry>(StringComparer.Ordinal);
        var name = Path.GetFileNameWithoutExtension(file);

        using var stream = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        using var reader = new StreamReader(stream);

        if (Path.GetExtension(file).Equals(".jsonl", StringComparison.OrdinalIgnoreCase))
        {
            DateTime? last = null;
            while (reader.ReadLine() is { } line)
            {
                if (line.Length == 0) continue;
                try
                {
                    using var doc = JsonDocument.Parse(line);
                    Absorb(doc.RootElement, name, ref last, byId);
                }
                catch (JsonException) { /* ligne tronquée : Gemini écrit pendant qu'on lit */ }
            }
        }
        else
        {
            try
            {
                using var doc = JsonDocument.Parse(reader.ReadToEnd());
                var root = doc.RootElement;
                DateTime? start = Timestamp(root, "startTime");
                if (root.TryGetProperty("messages", out var messages)
                    && messages.ValueKind == JsonValueKind.Array)
                {
                    foreach (var message in messages.EnumerateArray())
                    {
                        Absorb(message, name, ref start, byId);
                    }
                }
            }
            catch (JsonException) { /* document en cours d'écriture */ }
        }

        return byId.Values.Where(e => e.Timestamp >= since).ToList();
    }

    /// <summary>
    /// Prend en compte un message s'il porte des jetons.
    /// </summary>
    /// <remarks>
    /// Correspondance des compteurs, qui préserve le sens de <c>total</c> :
    /// <c>input</c> est le prompt entier, <c>cached</c> en est un sous-ensemble — on le soustrait
    /// pour ne pas compter deux fois. <c>thoughts</c> (raisonnement) fait déjà partie de la sortie
    /// côté modèle mais est compté à part par Gemini, donc on l'ajoute à la sortie. Gemini ne
    /// distingue pas d'écriture de cache.
    /// </remarks>
    private static void Absorb(JsonElement message, string file, ref DateTime? fallback,
                               Dictionary<string, UsageAggregator.UsageEntry> byId)
    {
        if (message.ValueKind != JsonValueKind.Object) return;

        if (Timestamp(message, "timestamp") is { } own) fallback = own;
        if (!message.TryGetProperty("tokens", out var tokens)
            || tokens.ValueKind != JsonValueKind.Object) return;
        if (fallback is not { } date) return;

        var id = message.TryGetProperty("id", out var rawId) ? rawId.GetString() ?? "" : "";
        if (id.Length == 0) id = date.Ticks.ToString(CultureInfo.InvariantCulture);

        var input = Number(tokens, "input");
        var cached = Number(tokens, "cached");

        byId[id] = new UsageAggregator.UsageEntry(
            Key: $"gemini|{file}|{id}",
            Timestamp: date,
            Model: message.TryGetProperty("model", out var m) ? m.GetString() ?? "" : "",
            Input: Math.Max(0, input - cached) + Number(tokens, "tool"),
            Output: Number(tokens, "output") + Number(tokens, "thoughts"),
            CacheWrite: 0,
            CacheRead: cached);
    }

    private static DateTime? Timestamp(JsonElement element, string name)
    {
        if (!element.TryGetProperty(name, out var raw) || raw.ValueKind != JsonValueKind.String) return null;
        return DateTimeOffset.TryParse(raw.GetString(), CultureInfo.InvariantCulture,
                                       DateTimeStyles.AdjustToUniversal, out var utc)
            ? utc.LocalDateTime
            : null;
    }

    private static long Number(JsonElement parent, string name) =>
        parent.TryGetProperty(name, out var value) && value.TryGetInt64(out var n) ? n : 0;
}
