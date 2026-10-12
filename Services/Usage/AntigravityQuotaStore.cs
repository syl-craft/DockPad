using System.IO;
using System.Text.Json;

namespace DockPad.Services.Usage;

/// <summary>
/// Le dernier relevé du quota Antigravity, gardé dans le profil (<c>antigravity-quota.json</c>).
/// </summary>
/// <remarks>
/// Le jeton ne vit qu'une heure et DockPad ne le renouvelle pas : sans ce fichier, la jauge
/// resterait vide après chaque redémarrage jusqu'au prochain usage d'Antigravity. Il ne contient
/// que des pourcentages et des heures — jamais le jeton.
/// </remarks>
public static class AntigravityQuotaStore
{
    public const string FileName = "antigravity-quota.json";

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
    };

    /// <summary>Le relevé enregistré, ou <c>null</c> s'il n'y en a pas ou s'il est illisible.</summary>
    public static AntigravityReading? Load(string path)
    {
        if (!File.Exists(path)) return null;

        try
        {
            return JsonSerializer.Deserialize<AntigravityReading>(File.ReadAllText(path), JsonOpts);
        }
        catch (Exception ex) when (ex is JsonException or IOException or NotSupportedException or UnauthorizedAccessException)
        {
            // Un cache abîmé ou inaccessible n'est pas une panne : la prochaine lecture réussie le
            // réécrit. Laisser l'erreur remonter ferait disparaître l'onglet entier.
            LogService.Warn(ex, "Lecture du dernier relevé du quota Antigravity");
            return null;
        }
    }

    public static void Save(string path, AntigravityReading reading)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path) ?? ".");
        File.WriteAllText(path, JsonSerializer.Serialize(reading, JsonOpts));
    }
}
