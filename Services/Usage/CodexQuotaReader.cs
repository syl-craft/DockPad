using System.Globalization;
using System.Text.Json;
using DockPad.Models;

namespace DockPad.Services.Usage;

/// <summary>Dernier relevé du compte Codex ; les deux fenêtres peuvent être absentes.</summary>
public sealed record CodexQuotaSnapshot(DateTimeOffset ObservedAt, UsageWindow? Session, UsageWindow? Week);

/// <summary>Extrait payload.rate_limits des événements token_count.</summary>
public static class CodexQuotaReader
{
    /// <summary>
    /// Au-delà, le relevé reste affiché mais daté. Codex n'a pas d'API de quota : ce relevé ne se
    /// renouvelle que quand on s'en sert, et le masquer faisait disparaître la jauge un quart
    /// d'heure après chaque session, soit la plupart du temps. Un relevé ancien ne peut que
    /// sous-estimer — le chiffre ne monte que si Codex est utilisé.
    /// </summary>
    public static readonly TimeSpan FreshFor = TimeSpan.FromMinutes(15);

    public static CodexQuotaSnapshot? ParseLine(string line)
    {
        try
        {
            using var doc = JsonDocument.Parse(line);
            var root = doc.RootElement;
            if (!Object(root, "payload", out var payload)
                || !StringEquals(root, "type", "event_msg")
                || !StringEquals(payload, "type", "token_count")
                || !Object(payload, "rate_limits", out var limits)) return null;

            // Accepte le quota global "codex" et les relevés sans identifiant.
            if (limits.TryGetProperty("limit_id", out var id) && id.ValueKind != JsonValueKind.Null
                && (id.ValueKind != JsonValueKind.String || id.GetString() != "codex")) return null;
            if (!root.TryGetProperty("timestamp", out var timestamp) || timestamp.ValueKind != JsonValueKind.String
                || !DateTimeOffset.TryParse(timestamp.GetString(), CultureInfo.InvariantCulture,
                    DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var observed)) return null;

            UsageWindow? session = null, week = null;
            foreach (var field in new[] { "primary", "secondary" })
            {
                if (!Object(limits, field, out var window)
                    || !window.TryGetProperty("window_minutes", out var duration)
                    || duration.ValueKind != JsonValueKind.Number || !duration.TryGetInt32(out var minutes)) continue;
                // Classe les fenêtres par durée : 5 h ou 7 jours.
                if (minutes == 300) session ??= Window(window);
                if (minutes == 10080) week ??= Window(window);
            }
            return new CodexQuotaSnapshot(observed, session, week);
        }
        catch (JsonException) { return null; }
    }

    /// <summary>Écarte les relevés futurs et les fenêtres expirées ; date les relevés anciens.</summary>
    public static CodexQuotaSnapshot? Current(CodexQuotaSnapshot? snapshot, DateTime now)
    {
        if (snapshot is null) return null;
        var utc = new DateTimeOffset(now.ToUniversalTime());
        var age = utc - snapshot.ObservedAt;
        if (age < TimeSpan.Zero) return null;
        DateTime? observed = age > FreshFor ? snapshot.ObservedAt.LocalDateTime : null;

        // Une fenêtre sans heure de remise à zéro ne dit pas si elle court encore : ancienne,
        // elle affirmerait un chiffre qui a peut-être déjà repassé à zéro.
        UsageWindow? Fresh(UsageWindow? window) => window switch
        {
            null => null,
            { ResetsAt: { } reset } when reset.ToUniversalTime() <= utc.UtcDateTime => null,
            { ResetsAt: null } when observed is not null => null,
            _ => observed is null ? window : new UsageWindow
            {
                UsedPct = window.UsedPct, ResetsAt = window.ResetsAt, ObservedAt = observed,
            },
        };
        return snapshot with { Session = Fresh(snapshot.Session), Week = Fresh(snapshot.Week) };
    }

    private static UsageWindow? Window(JsonElement value)
    {
        if (!value.TryGetProperty("used_percent", out var pct) || pct.ValueKind != JsonValueKind.Number
            || !pct.TryGetDouble(out var used) || !double.IsFinite(used)) return null;

        DateTime? resetsAt = null;
        if (value.TryGetProperty("resets_at", out var reset) && reset.ValueKind != JsonValueKind.Null)
        {
            if (reset.ValueKind != JsonValueKind.Number || !reset.TryGetInt64(out var seconds)) return null;
            try { resetsAt = DateTimeOffset.FromUnixTimeSeconds(seconds).LocalDateTime; }
            catch (ArgumentOutOfRangeException) { return null; }
        }
        return new UsageWindow
        {
            UsedPct = (int)Math.Round(Math.Clamp(used, 0, 100), MidpointRounding.AwayFromZero),
            ResetsAt = resetsAt,
        };
    }

    private static bool Object(JsonElement parent, string name, out JsonElement value)
    {
        value = default;
        return parent.ValueKind == JsonValueKind.Object && parent.TryGetProperty(name, out value)
            && value.ValueKind == JsonValueKind.Object;
    }

    private static bool StringEquals(JsonElement parent, string name, string expected) =>
        parent.ValueKind == JsonValueKind.Object && parent.TryGetProperty(name, out var value)
        && value.ValueKind == JsonValueKind.String && value.GetString() == expected;
}
