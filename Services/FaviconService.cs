using System.IO;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using DockPad.Models;

namespace DockPad.Services;

/// <summary>
/// Récupère l'icône d'un site pour une tuile <see cref="ShortcutType.OpenUrl"/> qui n'en a pas.
/// </summary>
/// <remarks>
/// <para>
/// <b>Seul le domaine quitte la machine.</b> Le service de favicons interrogé prend un domaine en
/// paramètre, et c'est tout ce qu'on lui donne : ni le chemin, ni la chaîne de requête. Un
/// identifiant de projet ou un numéro de client dans une URL interne n'a rien à faire chez un
/// tiers, et un test le vérifie.
/// </para>
/// <para>
/// <b>Ce chemin n'est jamais critique.</b> Hors ligne, DNS cassé, proxy d'entreprise, service
/// indisponible : tout se traduit par <c>null</c>, donc par une tuile qui garde l'icône du
/// navigateur — le comportement d'avant cette fonctionnalité. Une icône manquante n'est pas une
/// panne et ne s'affiche pas comme telle.
/// </para>
/// </remarks>
public sealed class FaviconService
{
    /// <summary>Taille demandée : 128 px, pour une tuile qui affiche 36 px sur un écran HiDPI.</summary>
    private const int Size = 128;

    /// <summary>
    /// Court exprès : c'est un agrément posé sur le chemin de sauvegarde d'une tuile. Personne
    /// n'attend cinq secondes de plus pour une icône.
    /// </summary>
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(5);

    private readonly HttpClient _http;

    public FaviconService(HttpClient? http = null)
    {
        _http = http ?? new HttpClient { Timeout = Timeout };
    }

    /// <summary>
    /// Hôte d'une URL web, ou <c>null</c> si ce n'en est pas une.
    /// </summary>
    /// <remarks>
    /// Le filtre sur le schéma n'est pas cosmétique : une commande de tuile peut être un chemin de
    /// fichier ou une URL <c>file:</c>, et rien de tout cela ne doit partir sur le réseau.
    /// </remarks>
    public static string? DomainOf(string? url)
    {
        if (string.IsNullOrWhiteSpace(url)) return null;
        if (!Uri.TryCreate(url.Trim(), UriKind.Absolute, out var uri)) return null;
        if (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps) return null;

        return string.IsNullOrEmpty(uri.Host) ? null : uri.Host;
    }

    /// <summary>Adresse interrogée pour un domaine donné.</summary>
    public static string BuildUrl(string domain) =>
        $"https://www.google.com/s2/favicons?domain={Uri.EscapeDataString(domain)}&sz={Size}";

    /// <summary>
    /// Faut-il aller chercher une icône ? Décision pure, testable sans réseau ni registre.
    /// </summary>
    /// <param name="enabled">Le réglage des Options. Décoché, aucune requête ne part.</param>
    /// <param name="iconPath">
    /// Icône déjà choisie. Non vide, elle gagne : on ne remplace jamais un choix de l'utilisateur.
    /// </param>
    public static bool ShouldFetch(bool enabled, ShortcutType type, string? iconPath, string? command)
    {
        if (!enabled) return false;
        if (type != ShortcutType.OpenUrl) return false;
        if (!string.IsNullOrWhiteSpace(iconPath)) return false;

        return DomainOf(command) is not null;
    }

    /// <summary>
    /// Télécharge l'icône du site et l'écrit dans un fichier temporaire, dont le chemin est rendu.
    /// <c>null</c> pour toute autre issue — c'est le cas normal, pas une erreur.
    /// </summary>
    /// <remarks>
    /// Un fichier temporaire, et non le store d'icônes, parce que la mise en store appartient à
    /// <see cref="IconStoreService.CopyToProfile"/> : lui seul sait dédupliquer.
    /// </remarks>
    public async Task<string?> TryDownloadAsync(string? url, CancellationToken ct)
    {
        if (DomainOf(url) is not { } domain) return null;

        try
        {
            using var response = await _http.GetAsync(BuildUrl(domain), ct).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                LogService.Info($"Favicon indisponible pour {domain} : HTTP {(int)response.StatusCode}");
                return null;
            }

            var bytes = await response.Content.ReadAsByteArrayAsync(ct).ConfigureAwait(false);
            // Zéro octet n'est pas une icône : la mettre dans le store donnerait une tuile vide, ce
            // qui est pire que l'icône du navigateur.
            if (bytes.Length == 0) return null;

            var path = Path.Combine(Path.GetTempPath(), $"dockpad-favicon-{Guid.NewGuid():N}.png");
            await File.WriteAllBytesAsync(path, bytes, ct).ConfigureAwait(false);
            return path;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            // Le domaine, jamais l'URL complète — même dans le journal.
            LogService.Warn(ex, $"Téléchargement du favicon de {domain}");
            return null;
        }
    }

    /// <summary>
    /// Télécharge l'icône et la range dans le store, rendant son chemin relatif au profil.
    /// </summary>
    /// <remarks>
    /// Composition des deux étapes, en un seul endroit : le dialogue et le service d'actions en ont
    /// besoin tous les deux, et la dupliquer voudrait dire deux endroits où oublier d'effacer le
    /// fichier temporaire.
    /// </remarks>
    public async Task<string?> TryFetchIntoStoreAsync(string? url, CancellationToken ct)
    {
        var temp = await TryDownloadAsync(url, ct).ConfigureAwait(false);
        if (temp is null) return null;

        try { return IconStoreService.CopyToProfile(temp); }
        finally { try { File.Delete(temp); } catch (IOException) { /* %TEMP% s'en occupera */ } }
    }
}
