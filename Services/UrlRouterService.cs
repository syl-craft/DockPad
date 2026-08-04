using System.Diagnostics;
using DockPad.Models;

namespace DockPad.Services;

/// <summary>
/// Route les URLs reçues : règle de domaine → lancement direct, sinon popup de choix.
/// </summary>
public static class UrlRouterService
{
    /// <summary>Host d'une URL en minuscules, ou null si non extractible.</summary>
    public static string? ExtractHost(string url) =>
        Uri.TryCreate(url, UriKind.Absolute, out var uri) && !string.IsNullOrEmpty(uri.Host)
            ? uri.Host.ToLowerInvariant()
            : null;

    /// <summary>Match exact ou sous-domaine : la règle "github.com" matche "gist.github.com".</summary>
    public static bool HostMatches(string host, string ruleHost) =>
        host.Equals(ruleHost, StringComparison.OrdinalIgnoreCase)
        || host.EndsWith("." + ruleHost, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Lance le navigateur avec l'URL. Si Arguments contient "%1" il est substitué,
    /// sinon l'URL (entre guillemets) est ajoutée en fin. Retourne false en cas d'échec
    /// (exe introuvable…) après affichage d'un AppDialog d'erreur.
    /// </summary>
    public static bool Launch(BrowserEntry browser, string url)
    {
        string args = browser.Arguments.Contains("%1")
            ? browser.Arguments.Replace("%1", $"\"{url}\"")
            : string.IsNullOrWhiteSpace(browser.Arguments)
                ? $"\"{url}\""
                : $"{browser.Arguments} \"{url}\"";

        try
        {
            Process.Start(new ProcessStartInfo(browser.ExePath, args) { UseShellExecute = false });
            return true;
        }
        catch (Exception ex)
        {
            AppDialog.Error($"Impossible de lancer {browser.Name} :\n{browser.ExePath}\n\n{ex.Message}",
                            "Navigateur introuvable");
            return false;
        }
    }
}
