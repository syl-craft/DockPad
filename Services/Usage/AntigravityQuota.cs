using System.Globalization;
using System.Text.Json;
using DockPad.Models;

namespace DockPad.Services.Usage;

/// <summary>Un réservoir de quota Antigravity : ce qui est consommé, et quand il repart à zéro.</summary>
public sealed record AntigravityPool(int UsedPct, DateTimeOffset ResetsAt);

/// <summary>
/// Un relevé du quota Antigravity. <c>null</c> sur un réservoir = absent de la réponse.
/// </summary>
/// <remarks>
/// Gardé sur le disque (<see cref="AntigravityQuotaStore"/>) : ce sont des pourcentages et des
/// heures, jamais le jeton.
/// </remarks>
public sealed record AntigravityReading(DateTimeOffset ObservedAt, AntigravityPool? Gemini, AntigravityPool? ThirdParty);

/// <summary>
/// Décisions pures du quota Antigravity : quel jeton utiliser, quel projet interroger, comment
/// regrouper les modèles en réservoirs, et ce qu'un relevé permet encore d'affirmer.
/// </summary>
/// <remarks>
/// Le quota est exposé <b>par modèle</b>, mais les modèles d'un même éditeur partagent un seul
/// réservoir : tous les Gemini portent la même fraction et la même heure de remise à zéro. Les
/// regrouper par <c>modelProvider</c> donne deux jauges — Google, et les modèles tiers (Anthropic,
/// OpenAI) — au lieu de vingt-cinq lignes identiques.
/// </remarks>
public static class AntigravityQuota
{
    /// <summary>Cible du Gestionnaire d'identification Windows où le CLI range son jeton.</summary>
    public const string CredentialTarget = "gemini:antigravity";

    /// <summary>Libellés des deux jauges : des noms de produits, donc jamais traduits.</summary>
    public const string GeminiLabel = "Gemini";
    public const string ThirdPartyLabel = "Claude · GPT";

    /// <summary>
    /// Âge au-delà duquel un relevé est daté à l'écran. Il reste affiché tant que sa fenêtre court :
    /// le quota ne bouge que si l'on utilise Antigravity, un relevé ancien ne peut donc que
    /// sous-estimer la consommation — même règle que pour Codex.
    /// </summary>
    public static readonly TimeSpan FreshFor = TimeSpan.FromMinutes(15);

    /// <summary>Un jeton qui expire pendant le vol ne sert à rien : même marge que pour Claude.</summary>
    private static readonly TimeSpan ExpiryMargin = TimeSpan.FromMinutes(1);

    private const string GoogleProvider = "MODEL_PROVIDER_GOOGLE";

    /// <summary>
    /// Jeton d'accès de l'identifiant, ou <c>null</c> s'il est absent, vide, illisible ou expiré.
    /// Jamais renouvelé ici : le CLI le fait lui-même chaque fois qu'on s'en sert.
    /// </summary>
    public static string? ReadAccessToken(string? credentialJson, DateTimeOffset now)
    {
        if (string.IsNullOrEmpty(credentialJson)) return null;

        try
        {
            using var doc = JsonDocument.Parse(credentialJson);
            if (doc.RootElement.ValueKind != JsonValueKind.Object) return null;
            if (!doc.RootElement.TryGetProperty("token", out var token)) return null;
            // Un JSON `null` explicite se décode en élément présent : même piège que chez Claude.
            if (token.ValueKind != JsonValueKind.Object) return null;

            var accessToken = token.TryGetProperty("access_token", out var raw) && raw.ValueKind == JsonValueKind.String
                ? raw.GetString()
                : null;
            if (string.IsNullOrEmpty(accessToken)) return null;

            var expiry = ReadDate(token, "expiry");
            if (expiry != null && expiry.Value <= now + ExpiryMargin) return null;

            return accessToken;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>
    /// Projet rendu par <c>loadCodeAssist</c>, sous sa forme chaîne ou objet <c>{ id }</c>, ou
    /// <c>null</c>. Sans lui, <c>fetchAvailableModels</c> répond 403.
    /// </summary>
    public static string? ReadProject(string loadCodeAssistJson)
    {
        try
        {
            using var doc = JsonDocument.Parse(loadCodeAssistJson);
            if (doc.RootElement.ValueKind != JsonValueKind.Object) return null;
            if (!doc.RootElement.TryGetProperty("cloudaicompanionProject", out var project)) return null;

            var id = project.ValueKind switch
            {
                JsonValueKind.String => project.GetString(),
                JsonValueKind.Object when project.TryGetProperty("id", out var raw) && raw.ValueKind == JsonValueKind.String
                    => raw.GetString(),
                _ => null,
            };
            return string.IsNullOrEmpty(id) ? null : id;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>
    /// Regroupe la réponse de <c>fetchAvailableModels</c> en deux réservoirs, ou rend <c>null</c>
    /// si aucun modèle ne porte de quota.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Seuls comptent les modèles qui ont une heure de remise à zéro : les fonctions internes
    /// (<c>tab_*</c>, <c>chat_*</c>) n'en ont pas, et ne sont pas le quota de l'utilisateur.
    /// </para>
    /// <para>
    /// Un réservoir prend la fraction restante <b>la plus basse</b> de ses modèles : c'est celle qui
    /// bloquera en premier. Une fraction absente vaut zéro — proto3 n'écrit pas un champ nul, et
    /// c'est donc précisément ainsi qu'arrive un quota épuisé.
    /// </para>
    /// </remarks>
    public static AntigravityReading? ParseModels(string json, DateTimeOffset observedAt)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.ValueKind != JsonValueKind.Object) return null;
            if (!doc.RootElement.TryGetProperty("models", out var models) || models.ValueKind != JsonValueKind.Object)
                return null;

            AntigravityPool? gemini = null;
            AntigravityPool? thirdParty = null;

            foreach (var model in models.EnumerateObject())
            {
                var pool = PoolOf(model.Value);
                if (pool == null) continue;

                if (IsGoogleModel(model.Value)) gemini = LowestRemaining(gemini, pool);
                else thirdParty = LowestRemaining(thirdParty, pool);
            }

            return gemini == null && thirdParty == null ? null : new AntigravityReading(observedAt, gemini, thirdParty);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>
    /// Les deux jauges qu'un relevé permet encore d'afficher : Gemini dans le créneau de session,
    /// les modèles tiers dans celui de la semaine, chacune nommée.
    /// </summary>
    /// <remarks>
    /// Une fenêtre dont la remise à zéro est passée est masquée : le relevé décrivait une
    /// consommation déjà effacée. Un relevé plus vieux que <see cref="FreshFor"/> est daté.
    /// </remarks>
    public static (UsageWindow? Gemini, UsageWindow? ThirdParty) Windows(AntigravityReading? reading, DateTimeOffset now)
    {
        if (reading == null) return (null, null);

        DateTime? observedAt = now - reading.ObservedAt > FreshFor ? reading.ObservedAt.LocalDateTime : null;

        return (WindowOf(reading.Gemini, GeminiLabel, observedAt, now),
                WindowOf(reading.ThirdParty, ThirdPartyLabel, observedAt, now));
    }

    private static UsageWindow? WindowOf(AntigravityPool? pool, string label, DateTime? observedAt, DateTimeOffset now)
    {
        if (pool == null || pool.ResetsAt <= now) return null;

        return new UsageWindow
        {
            UsedPct = pool.UsedPct,
            ResetsAt = pool.ResetsAt.LocalDateTime,
            ObservedAt = observedAt,
            Label = label,
        };
    }

    private static AntigravityPool? PoolOf(JsonElement model)
    {
        if (model.ValueKind != JsonValueKind.Object) return null;
        if (!model.TryGetProperty("quotaInfo", out var quota) || quota.ValueKind != JsonValueKind.Object) return null;

        var resetsAt = ReadDate(quota, "resetTime");
        if (resetsAt == null) return null;

        var remaining = quota.TryGetProperty("remainingFraction", out var raw) && raw.ValueKind == JsonValueKind.Number
            ? raw.GetDouble()
            : 0;

        var usedPct = (int)Math.Clamp(Math.Round((1 - remaining) * 100), 0, 100);
        return new AntigravityPool(usedPct, resetsAt.Value);
    }

    private static bool IsGoogleModel(JsonElement model) =>
        model.TryGetProperty("modelProvider", out var provider)
        && provider.ValueKind == JsonValueKind.String
        && provider.GetString() == GoogleProvider;

    private static AntigravityPool LowestRemaining(AntigravityPool? current, AntigravityPool candidate) =>
        current == null || candidate.UsedPct > current.UsedPct ? candidate : current;

    private static DateTimeOffset? ReadDate(JsonElement element, string property)
    {
        if (!element.TryGetProperty(property, out var raw) || raw.ValueKind != JsonValueKind.String) return null;

        return DateTimeOffset.TryParse(raw.GetString(), CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed)
            ? parsed
            : null;
    }
}
