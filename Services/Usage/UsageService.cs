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
        var entries = EntriesOf(config);

        // Un fournisseur absent de la config est interrogé : au premier lancement, le fichier est
        // vide et rien ne s'afficherait sinon.
        var visible = _providers
            .Where(p => IsHidden(entries, p.Id) == false)
            .OrderBy(p => OrderOf(entries, p.Id))
            .ToList();

        var reads = visible.Select(p => ReadAsync(p, ct)).ToList();
        var results = await Task.WhenAll(reads).ConfigureAwait(false);

        return results.Where(u => u is not null).Select(u => u!).ToList();
    }

    /// <summary>
    /// Instantanés déjà lus que la config laisse visibles, dans son ordre, sans relire personne.
    /// </summary>
    public static List<AiUsage> KeepVisible(IReadOnlyList<AiUsage> snapshots, UsageConfig config)
    {
        var entries = EntriesOf(config);

        return snapshots
            .Where(s => IsHidden(entries, s.ProviderId) == false)
            .OrderBy(s => OrderOf(entries, s.ProviderId))
            .ToList();
    }

    private static Dictionary<string, AiProviderEntry> EntriesOf(UsageConfig config) =>
        config.Providers.ToDictionary(p => p.Id, StringComparer.OrdinalIgnoreCase);

    private static bool IsHidden(Dictionary<string, AiProviderEntry> entries, string providerId) =>
        entries.TryGetValue(providerId, out var entry) && entry.Hidden;

    private static int OrderOf(Dictionary<string, AiProviderEntry> entries, string providerId) =>
        entries.TryGetValue(providerId, out var entry) ? entry.Order : int.MaxValue;

    /// <summary>
    /// Identifiants des fournisseurs connus, masqués compris.
    /// </summary>
    public IReadOnlyList<string> ProviderIds => _providers.Select(p => p.Id).ToList();

    /// <summary>
    /// Lit un seul fournisseur, même masqué dans le bandeau. <c>null</c> = inconnu, absent du poste
    /// ou en échec.
    /// </summary>
    public async Task<AiUsage?> ReadOneAsync(string providerId, CancellationToken ct)
    {
        var provider = _providers
            .Where(p => string.Equals(p.Id, providerId, StringComparison.OrdinalIgnoreCase))
            .FirstOrDefault();
        if (provider == null) return null;

        return await ReadAsync(provider, ct).ConfigureAwait(false);
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
