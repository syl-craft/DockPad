using System.Threading;
using System.Threading.Tasks;
using DockPad.Services;

namespace DockPad.Secrets;

/// <summary>Ce qu'un envoi a produit : la cible, les noms écrits, et ceux que GitHub a refusés.</summary>
public sealed record GitHubSendOutcome(
    GitHubTargetKind Kind, string Target, IReadOnlyList<string> Sent, IReadOnlyList<string> Refused);

/// <summary>
/// Les appels à <c>gh</c> d'un inventaire : vérifier sans rien déverrouiller, puis envoyer.
/// </summary>
/// <remarks>
/// <para>
/// <b>La vérification ne touche pas au coffre</b> : elle compare des noms. C'est ce qui permet de
/// l'afficher avant de réclamer le mot de passe maître, et de la relancer à volonté pour surveiller
/// l'âge d'une clé qui expire.
/// </para>
/// <para>
/// <b>Un refus de GitHub n'arrête pas les autres lignes.</b> Le rendu est déjà tout ou rien — une
/// valeur manquante ne part jamais — mais un refus réseau sur la troisième ligne ne rend pas fausses
/// les deux premières. Il est nommé, et l'écran passe en ambre.
/// </para>
/// </remarks>
public static class GitHubSyncService
{
    /// <summary>La cible lisible : <c>propriétaire/dépôt</c>, et l'environnement s'il y en a un.</summary>
    public static string TargetLabel(GitHubInventory inventory) =>
        inventory.Environment == null
            ? inventory.Repo
            : Loc.F("GitHub_Target_Environment", inventory.Repo, inventory.Environment);

    /// <summary>
    /// <c>gh</c> est-il là et connecté ? Rien n'est lu ni écrit sur GitHub.
    /// </summary>
    public static async Task<(string? Exe, InjectionReport? Failure)> PreflightAsync(CancellationToken token)
    {
        var exe = GitHubCli.Locate();
        if (exe == null) return (null, InjectionReport.Fail(Loc.T("GitHub_Error_CliMissing")));

        var status = await GitHubCli.RunAsync(exe, ["auth", "status"], token).ConfigureAwait(false);

        return status.Ok
            ? (exe, null)
            : (null, InjectionReport.Fail(Loc.T("GitHub_Error_NotLoggedIn"), status.Stderr.Trim()));
    }

    /// <summary>Compare l'inventaire à ce que GitHub porte déjà. Des noms et des dates, jamais une valeur.</summary>
    public static async Task<(GitHubCheck? Check, InjectionReport? Failure)> CheckAsync(
        GitHubInventory inventory, CancellationToken token)
    {
        var (exe, refused) = await PreflightAsync(token).ConfigureAwait(false);
        if (exe == null) return (null, refused);

        var listed = await GitHubCli.RunAsync(exe,
            GitHubCli.ListArguments(inventory.Kind, inventory.Repo, inventory.Environment), token).ConfigureAwait(false);

        if (!listed.Ok)
            return (null, InjectionReport.Fail(
                Loc.F("GitHub_Error_ListFailed", TargetLabel(inventory)), listed.Stderr.Trim()));

        return (inventory.Compare(GitHubCli.ParseList(listed.Stdout)), null);
    }

    /// <summary>Envoie chaque valeur par l'entrée standard de <c>gh … set</c>, une ligne après l'autre.</summary>
    public static async Task<GitHubSendOutcome> SendAsync(
        GitHubInventory inventory, IReadOnlyList<GitHubValue> values, CancellationToken token)
    {
        var exe = GitHubCli.Locate()
            ?? throw new InvalidOperationException("The GitHub CLI disappeared between the check and the send.");

        var sent = new List<string>();
        var refused = new List<string>();

        foreach (var value in values)
        {
            var result = await GitHubCli.RunAsync(exe,
                GitHubCli.SetArguments(inventory.Kind, value.Name, inventory.Repo, inventory.Environment),
                token, stdin: value.Value).ConfigureAwait(false);

            if (result.Ok)
            {
                sent.Add(value.Name);
                continue;
            }

            // Le nom, le code et l'erreur standard : gh n'y recopie pas la valeur, qui n'est jamais
            // passée en argument.
            LogService.Info($"gh a refusé {value.Name} (code {result.ExitCode}) : {result.Stderr.Trim()}");
            refused.Add(value.Name);
        }

        LogService.Info($"Envoi vers GitHub ({inventory.Repo}) : {sent.Count} écrit(s), {refused.Count} refusé(s)");

        return new GitHubSendOutcome(inventory.Kind, TargetLabel(inventory), sent, refused);
    }
}
