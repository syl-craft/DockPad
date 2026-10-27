using System.IO;
using System.Text.Json;
using DockPad.Models;

namespace DockPad.Services;

/// <summary>
/// Réglages du bandeau Usage IA et notification après sauvegarde réussie.
/// </summary>
public static class UsageConfigService
{
    public static readonly string FilePath = AppPaths.File("usage.json");
    public static event Action? Changed;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    public static UsageConfig Load() => Load(FilePath);

    public static UsageConfig Load(string path)
    {
        return JsonConfigFile.Load<UsageConfig>(path, JsonOptions, config =>
        {
            // Une entrée sans id n'a pas de clé de fusion : inexploitable, mais elle ne doit pas
            // emporter le reste du fichier avec elle.
            config.Providers.RemoveAll(p => string.IsNullOrWhiteSpace(p.Id));

            // Les doublons d'id sont écartés ici, à la seule porte d'entrée du fichier. Plus loin,
            // la fusion et l'agrégation indexent cette liste par id : un doublon y lèverait, et
            // l'exception, attrapée plus haut, masquerait le bandeau entier — exactement ce que la
            // tolérance ci-dessus cherche à éviter. La première entrée gagne.
            config.Providers = config.Providers
                .GroupBy(p => p.Id, StringComparer.OrdinalIgnoreCase)
                .Select(group => group.First())
                .ToList();

            // Raccourcir l'ancien libellé par défaut sans écraser les noms personnalisés.
            foreach (var provider in config.Providers.Where(p =>
                         string.Equals(p.Id, "claude", StringComparison.OrdinalIgnoreCase)
                         && p.DetectedName == "Claude Code"))
            {
                if (provider.Name == provider.DetectedName)
                    provider.Name = "Claude";
                provider.DetectedName = "Claude";
            }

            // Le fichier se modifie à la main : une valeur écrite hors de la fenêtre ne doit pas
            // contourner la liste blanche de la saisie, puisqu'elle finit dans une commande.
            if (!OllamaAgent.IsValidHarness(config.OllamaHarness ?? ""))
                config.OllamaHarness = "";
            if (!OllamaAgent.IsValidModel(config.OllamaModel ?? ""))
                config.OllamaModel = "";
        });
    }

    public static void Save(UsageConfig config) => Save(config, FilePath);

    public static void Save(UsageConfig config, string path)
    {
        JsonConfigFile.Save(path, config, JsonOptions);

        var subscribers = Changed;
        if (subscribers == null) return;
        foreach (Action subscriber in subscribers.GetInvocationList())
        {
            try { subscriber(); }
            catch (Exception ex) { LogService.Warn(ex, "Échec d’un abonné au changement de configuration Usage IA."); }
        }
    }
}
