using System.Threading;
using System.Threading.Tasks;
using DockPad.Models;

namespace DockPad.Services.Usage;

/// <summary>
/// Interroge les fournisseurs visibles et renvoie leurs instantanés, dans l'ordre de la config.
/// </summary>
/// <remarks>
/// La liste de fournisseurs est un paramètre de construction, avec le registre de production par
/// défaut : c'est ce qui permet aux tests et à l'outil de capture de substituer les leurs sans
/// toucher au registre.
/// </remarks>
public sealed class UsageService
{
    private readonly IReadOnlyList<IUsageProvider> _providers;

    public UsageService(IEnumerable<IUsageProvider>? providers = null)
    {
        _providers = (providers ?? UsageProviderRegistry.All).ToList();
    }

    /// <summary>
    /// Lit tous les fournisseurs non masqués, en parallèle. Un fournisseur lent ne retarde pas les
    /// autres ; un fournisseur qui échoue ou qui n'a rien à dire est simplement absent du résultat.
    /// </summary>
    public async Task<List<AiUsage>> RefreshAsync(UsageConfig config, CancellationToken ct)
    {
        var entries = config.Providers.ToDictionary(p => p.Id, StringComparer.OrdinalIgnoreCase);

        // Un fournisseur absent de la config est interrogé : au premier lancement, le fichier est
        // vide et rien ne s'afficherait sinon.
        var visible = _providers
            .Where(p => !(entries.TryGetValue(p.Id, out var entry) && entry.Hidden))
            .OrderBy(p => entries.TryGetValue(p.Id, out var entry) ? entry.Order : int.MaxValue)
            .ToList();

        var reads = visible.Select(p => ReadAsync(p, ct)).ToList();
        var results = await Task.WhenAll(reads).ConfigureAwait(false);

        return results.Where(u => u is not null).Select(u => u!).ToList();
    }

    private static async Task<AiUsage?> ReadAsync(IUsageProvider provider, CancellationToken ct)
    {
        try
        {
            return await provider.ReadAsync(ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;   // l'annulation n'est pas une panne du fournisseur
        }
        catch (Exception ex)
        {
            LogService.Warn(ex, $"Lecture de la consommation du fournisseur « {provider.Id} »");
            return null;
        }
    }
}
