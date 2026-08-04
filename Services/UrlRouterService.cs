using System.Diagnostics;
using DockPad.Models;

namespace DockPad.Services;

/// <summary>
/// Route les URLs reçues : règle de domaine → lancement direct, sinon popup de choix.
/// </summary>
public static class UrlRouterService
{
    private static readonly Queue<string> _pending = new();
    private static bool _pickerOpen;

    /// <summary>
    /// Traite une URL (à appeler sur le thread UI) : règle de domaine → lancement direct,
    /// sinon popup. Les URLs reçues pendant qu'une popup est ouverte sont mises en file
    /// et traitées à sa fermeture. Retourne la popup affichée pour cette URL, ou null si
    /// aucune fenêtre n'a été créée (lancement direct, ou mise en file d'attente).
    /// </summary>
    public static BrowserPickerWindow? Handle(string url)
    {
        if (string.IsNullOrWhiteSpace(url)) return null;

        var config = BrowserConfigService.EnsureInitialized();

        var host = ExtractHost(url);
        var rule = host is null ? null
            : config.Rules.FirstOrDefault(r => HostMatches(host, r.Host));
        var ruleBrowser = rule is null ? null
            : config.Browsers.FirstOrDefault(b => b.Id == rule.BrowserId && !b.Hidden);

        // Règle valide → lancement direct ; si le lancement échoue, on retombe sur la popup.
        if (ruleBrowser is not null && Launch(ruleBrowser, url))
        {
            DrainPending();
            return null;
        }

        if (_pickerOpen) { _pending.Enqueue(url); return null; }
        return ShowPicker(url, config);
    }

    private static BrowserPickerWindow ShowPicker(string url, Models.BrowsersConfig config)
    {
        _pickerOpen = true;
        var picker = new BrowserPickerWindow(url, config);
        picker.Closed += (_, _) =>
        {
            _pickerOpen = false;
            DrainPending();
        };
        picker.Show();
        picker.Activate();
        return picker;
    }

    /// <summary>Dépile et traite l'URL suivante en attente, s'il y en a et qu'aucune popup n'est ouverte.</summary>
    private static void DrainPending()
    {
        if (!_pickerOpen && _pending.Count > 0) Handle(_pending.Dequeue());
    }

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
