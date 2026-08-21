using System.Threading;
using System.Threading.Tasks;
using DockPad.Models;

namespace DockPad.Services.Usage;

/// <summary>
/// Un fournisseur de consommation IA. Détection et lecture au même endroit : ajouter un assistant,
/// c'est écrire une implémentation et l'inscrire dans <see cref="UsageProviderRegistry"/> — ces
/// deux points sont les seuls à toucher.
/// </summary>
public interface IUsageProvider
{
    /// <summary>Identifiant stable, clé de fusion dans usage.json (« claude », « demo »…).</summary>
    string Id { get; }

    /// <summary>Nom par défaut, avant toute personnalisation par l'utilisateur.</summary>
    string Name { get; }

    /// <summary>
    /// L'outil est-il installé et configuré, et où sont ses données ? Appelée uniquement sur
    /// ↻ Redétecter, jamais en tâche de fond.
    /// </summary>
    AiProbe Probe();

    /// <summary>
    /// Instantané de consommation, ou <c>null</c> s'il n'y a rien à afficher. <c>null</c> plutôt
    /// qu'une exception : « pas installé » est le cas normal, pas une erreur.
    /// </summary>
    Task<AiUsage?> ReadAsync(CancellationToken ct);
}
