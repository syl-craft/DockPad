using DockPad.Models;

namespace DockPad.Services.Usage;

/// <summary>
/// Détecte les fournisseurs installés et fusionne le résultat dans <c>usage.json</c>.
/// </summary>
/// <remarks>
/// Appelée uniquement sur ↻ Redétecter, jamais en tâche de fond — même règle que la détection des
/// profils de navigateur.
/// </remarks>
public static class AiDetectionService
{
    /// <summary>
    /// Sonde chaque fournisseur et renvoie la config fusionnée. Une sonde qui lève ne fait pas
    /// échouer la détection : le fournisseur est simplement porté non détecté.
    /// </summary>
    public static UsageConfig Detect(IEnumerable<IUsageProvider> providers, UsageConfig config)
    {
        var probes = new List<(IUsageProvider Provider, AiProbe Probe)>();
        foreach (var provider in providers)
        {
            try
            {
                probes.Add((provider, provider.Probe()));
            }
            catch (Exception ex)
            {
                LogService.Warn(ex, $"Détection du fournisseur IA « {provider.Id} »");
                probes.Add((provider, new AiProbe
                {
                    Available = false, DisplayName = provider.Name, Detail = "détection impossible",
                }));
            }
        }

        config.Providers = Merge(config.Providers, probes);
        return config;
    }

    /// <summary>
    /// Config du démarrage : au tout premier lancement, <c>usage.json</c> n'existe pas et n'a donc
    /// aucun fournisseur. On détecte une fois et on enregistre.
    /// </summary>
    /// <remarks>
    /// Sans ce démarrage à froid, <c>AiProbe.HiddenByDefault</c> n'agirait jamais : aucune fusion
    /// n'aurait eu lieu, et un fournisseur absent de la config est traité comme visible — le
    /// provider de démonstration s'afficherait donc dès la première ouverture. Même principe que la
    /// détection des navigateurs, qui se déclenche aussi quand la liste est vide.
    /// </remarks>
    public static UsageConfig LoadForStartup()
    {
        lock (ConfigLock.Gate)
        {
            var config = UsageConfigService.Load();
            if (!NeedsDetection(config)) return config;

            config = Detect(UsageProviderRegistry.All, config);
            UsageConfigService.Save(config);
            return config;
        }
    }

    /// <summary>
    /// Faut-il détecter au démarrage ? Oui au premier lancement (config vide), et oui quand le
    /// registre contient un fournisseur que la config ne connaît pas encore.
    /// </summary>
    /// <remarks>
    /// Ce second cas est celui d'une mise à jour de DockPad qui apporte de nouveaux fournisseurs :
    /// sans lui, ils n'apparaîtraient qu'après un ↻ Redétecter manuel, que personne ne pense à
    /// faire — la fonctionnalité serait livrée et invisible. Ce n'est pas une détection en tâche de
    /// fond pour autant : elle n'a lieu qu'une fois, jusqu'à ce que la config rattrape le registre.
    /// </remarks>
    private static bool NeedsDetection(UsageConfig config)
    {
        if (config.Providers.Count == 0) return true;

        var known = config.Providers.Select(p => p.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);
        return UsageProviderRegistry.All.Any(p => !known.Contains(p.Id));
    }

    /// <summary>
    /// Fusion additive, clé <c>Id</c>. Pure : c'est ici que vivent les règles de préservation, et
    /// c'est ce qui les rend testables sans toucher au disque.
    /// </summary>
    /// <remarks>
    /// <list type="bullet">
    /// <item>Masquage et ordre sont préservés.</item>
    /// <item>Un nom personnalisé est préservé ; un nom hérité suit le nom détecté.</item>
    /// <item>Un fournisseur absent des sondes est conservé, marqué non détecté.</item>
    /// <item>Une entrée inconnue du registre est conservée telle quelle (retour arrière de version).</item>
    /// <item><c>HiddenByDefault</c> n'agit qu'à la création de l'entrée.</item>
    /// </list>
    /// </remarks>
    public static List<AiProviderEntry> Merge(
        IReadOnlyList<AiProviderEntry> existing,
        IReadOnlyList<(IUsageProvider Provider, AiProbe Probe)> probes)
    {
        var result = existing.Select(Clone).ToList();
        var byId = result.ToDictionary(p => p.Id, StringComparer.OrdinalIgnoreCase);
        var probed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        var nextOrder = result.Count == 0 ? 0 : result.Max(p => p.Order) + 1;

        foreach (var (provider, probe) in probes)
        {
            probed.Add(provider.Id);
            var detectedName = probe.DisplayName.Length > 0 ? probe.DisplayName : provider.Name;

            if (byId.TryGetValue(provider.Id, out var entry))
            {
                // Le nom ne suit l'outil que s'il n'a pas été personnalisé dans DockPad.
                if (string.Equals(entry.Name, entry.DetectedName, StringComparison.Ordinal))
                    entry.Name = detectedName;

                entry.DetectedName = detectedName;
                entry.DataPath = probe.DataPath;
                entry.Detected = probe.Available;
                continue;
            }

            result.Add(new AiProviderEntry
            {
                Id = provider.Id,
                Name = detectedName,
                DetectedName = detectedName,
                DataPath = probe.DataPath,
                Detected = probe.Available,
                Hidden = probe.HiddenByDefault,
                Order = nextOrder++,
            });
        }

        // Un fournisseur qui n'a pas répondu reste dans le fichier : le supprimer détruirait son
        // masquage et son ordre pour une absence peut-être temporaire.
        foreach (var entry in result.Where(e => !probed.Contains(e.Id)))
        {
            entry.Detected = false;
        }

        return result.OrderBy(p => p.Order).ToList();
    }

    private static AiProviderEntry Clone(AiProviderEntry source) => new()
    {
        Id = source.Id,
        Name = source.Name,
        DetectedName = source.DetectedName,
        Hidden = source.Hidden,
        Order = source.Order,
        DataPath = source.DataPath,
        Detected = source.Detected,
    };
}
