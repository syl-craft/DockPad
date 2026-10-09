using System.Threading;
using System.Threading.Tasks;
using DockPad.Services;

namespace DockPad.Secrets;

/// <summary>Ce qu'un envoi a produit — et ce qu'il n'a pas pu affirmer.</summary>
/// <param name="Sent">Écrits, GitHub l'a confirmé.</param>
/// <param name="Refused">Refusés par GitHub : rien n'a été écrit pour eux.</param>
/// <param name="Uncertain">
/// En vol quand GitHub a cessé de répondre : la valeur a pu être écrite, ou non. Le dire vaut mieux
/// que de le ranger d'un côté ou de l'autre.
/// </param>
/// <param name="Unsent">Jamais tentés, l'envoi s'étant arrêté avant eux.</param>
public sealed record GitHubSendOutcome(
    GitHubTargetKind Kind, string Target, IReadOnlyList<string> Sent, IReadOnlyList<string> Refused,
    IReadOnlyList<string>? Uncertain = null, IReadOnlyList<string>? Unsent = null)
{
    /// <summary>Quelque chose a-t-il pu atteindre GitHub ? Faux = « rien n'a été envoyé » est vrai.</summary>
    public bool ReachedGitHub => Sent.Count > 0 || Uncertain is { Count: > 0 };
}

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
/// valeur manquante ne part jamais — mais un refus sur la troisième ligne ne rend pas fausses les
/// deux premières. Il est nommé, et l'écran passe en ambre.
/// </para>
/// </remarks>
public static class GitHubSyncService
{
    /// <summary>La cible lisible : <c>propriétaire/dépôt</c>, et l'environnement s'il y en a un.</summary>
    public static string TargetLabel(GitHubInventory inventory) =>
        inventory.Environment == null
            ? inventory.Repo
            : Loc.F("GitHub_Target_Environment", inventory.Repo, inventory.Environment);

    /// <summary><c>gh</c> est-il là et connecté ? Rien n'est lu ni écrit sur GitHub.</summary>
    public static async Task<(string? Exe, InjectionReport? Failure)> PreflightAsync(CancellationToken token)
    {
        var exe = GitHubCli.Locate(AppSettingsService.Current.GitHubCliPath);
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

        var remote = GitHubCli.ParseList(listed.Stdout);
        if (remote == null)
            return (null, InjectionReport.Fail(Loc.F("GitHub_Error_ListUnreadable", TargetLabel(inventory))));

        return (inventory.Compare(remote), null);
    }

    /// <summary>Envoie chaque valeur par l'entrée standard de <c>gh … set</c>, une ligne après l'autre.</summary>
    /// <remarks>
    /// <para>
    /// <b>L'erreur standard d'un refus ne va pas au journal</b> — seulement le nom et le code. C'est
    /// le flux qui a reçu la valeur ; même sans mode debug, rien ne garantit qu'une version future de
    /// <c>gh</c> ne l'y recopie pas.
    /// </para>
    /// <para>
    /// <b>Un délai dépassé n'efface pas ce qui est parti.</b> La ligne en vol est « incertaine », les
    /// suivantes « non envoyées » : rapporter « rien n'a été envoyé » après deux succès serait faux.
    /// Seule la fermeture de la fenêtre lève : il n'y a alors plus personne à qui rendre compte.
    /// </para>
    /// </remarks>
    public static async Task<GitHubSendOutcome> SendAsync(
        GitHubInventory inventory, IReadOnlyList<GitHubValue> values, CancellationToken token)
    {
        var exe = GitHubCli.Locate(AppSettingsService.Current.GitHubCliPath)
            ?? throw new InvalidOperationException("The GitHub CLI disappeared between the check and the send.");

        var sent = new List<string>();
        var refused = new List<string>();

        for (var index = 0; index < values.Count; index++)
        {
            var value = values[index];

            CliResult result;
            try
            {
                result = await GitHubCli.RunAsync(exe,
                    GitHubCli.SetArguments(inventory.Kind, value.Name, inventory.Repo, inventory.Environment),
                    token, stdin: value.Value).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (!token.IsCancellationRequested)
            {
                LogService.Info($"Envoi vers GitHub interrompu sur {value.Name} : délai dépassé");
                var unsent = values.Skip(index + 1).Select(v => v.Name).ToList();
                return Outcome(inventory, sent, refused, uncertain: [value.Name], unsent);
            }

            if (result.Ok)
            {
                sent.Add(value.Name);
                continue;
            }

            LogService.Info($"gh a refusé {value.Name} (code {result.ExitCode})");
            refused.Add(value.Name);
        }

        return Outcome(inventory, sent, refused, uncertain: [], unsent: []);
    }

    private static GitHubSendOutcome Outcome(GitHubInventory inventory,
        List<string> sent, List<string> refused, List<string> uncertain, List<string> unsent)
    {
        LogService.Info($"Envoi vers GitHub ({inventory.Repo}) : {sent.Count} écrit(s), {refused.Count} refusé(s), {uncertain.Count} incertain(s), {unsent.Count} non tenté(s)");

        return new GitHubSendOutcome(inventory.Kind, TargetLabel(inventory), sent, refused, uncertain, unsent);
    }
}
