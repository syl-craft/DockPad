using System.Diagnostics;

namespace DockPad.Services.Usage;

/// <summary>
/// Ouverture de la page de consommation d'un fournisseur, partagée par le bandeau et la barre.
/// </summary>
public static class UsagePageLink
{
    /// <summary>
    /// Vrai pour une adresse web absolue (http ou https). <c>Process.Start</c> avec <c>UseShellExecute</c> exécuterait
    /// aussi bien un chemin de fichier ou une commande : seule une page web passe.
    /// </summary>
    public static bool IsWebUrl(string? url, out Uri? uri)
    {
        uri = null;
        if (Uri.TryCreate(url, UriKind.Absolute, out var parsed) == false) return false;
        if (parsed.Scheme != Uri.UriSchemeHttp && parsed.Scheme != Uri.UriSchemeHttps) return false;

        uri = parsed;
        return true;
    }

    /// <summary>
    /// Ouvre la page dans le navigateur par défaut. Une adresse qui n'est pas une page web, ou un lancement qui échoue,
    /// n'ouvre rien.
    /// </summary>
    public static void Open(string? url)
    {
        if (IsWebUrl(url, out var uri) == false || uri == null) return;

        try
        {
            Process.Start(new ProcessStartInfo(uri.AbsoluteUri) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            LogService.Warn(ex, "Ouverture de la page de consommation du fournisseur");
        }
    }
}
