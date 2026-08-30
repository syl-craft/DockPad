using System.Threading;
using System.Threading.Tasks;
using DockPad.Services;

namespace DockPad.Secrets;

/// <summary>
/// Le coffre Bitwarden / Vaultwarden, vu par la CLI <c>bw</c>.
/// </summary>
/// <remarks>
/// <para>
/// <b>Tout ce qui sait « Bitwarden » vit ici</b>, à deux exceptions près et par découpage
/// délibéré : <see cref="BitwardenCli"/>, qui est le seul point à lancer un processus, et
/// <see cref="SecretVault"/>, qui est pur et se teste sans rien lancer. Ce fichier est ce qui les
/// enchaîne.
/// </para>
/// <para>
/// <b>Aucune clé de session n'est conservée.</b> Elle naît d'un <c>unlock</c>, vit le temps de
/// <see cref="OpenAsync"/>, et sort de portée avec la méthode. C'est le choix retenu : DockPad
/// démarre avec Windows et tourne des semaines, une clé de coffre n'a rien à y faire — au prix du
/// mot de passe maître à chaque injection.
/// </para>
/// <para>
/// <b>Quatre appels au plus</b>, dont un seul <c>list items</c> qui ramène tout. Le script d'origine
/// lançait une recherche par item ; ramener l'ensemble en un appel est plus rapide, et déplace la
/// résolution du côté testable de la frontière (voir <see cref="SecretVault"/>).
/// </para>
/// </remarks>
public sealed class BitwardenSecretSource : ISecretSource
{
    /// <summary>Destiné à devenir le préfixe de marqueur, celui que les fichiers portent déjà.</summary>
    public string Id => "bw";

    // ───────────── Décisions pures ─────────────

    /// <summary>
    /// Faut-il renoncer avant même de demander le mot de passe maître ?
    /// </summary>
    /// <remarks>
    /// Un seul statut est distingué, et c'est celui qui appelle une <b>autre action</b> de
    /// l'utilisateur : sans <c>bw login</c>, aucun mot de passe ne servirait à rien. Tout le reste —
    /// y compris un statut illisible parce que la CLI a changé de format — mène au déverrouillage,
    /// parce que sans clé de session la CLI ne peut rien lire de toute façon.
    /// </remarks>
    public static string? StatusFailure(string? status) =>
        status == "unauthenticated" ? Loc.T("Inject_Error_NotLoggedIn") : null;

    /// <summary>
    /// L'identifiant de l'organisation configurée, par nom ou par identifiant.
    /// </summary>
    /// <remarks>
    /// L'échec <b>liste les organisations disponibles</b> : sans elles, on ne sait pas si on s'est
    /// trompé de nom ou si l'organisation n'a jamais été créée, et ce sont deux corrections
    /// différentes.
    /// </remarks>
    public static (string? Id, string? Failure) ResolveOrganisation(
        IReadOnlyList<BwOrganization> organisations, string configured)
    {
        if (string.IsNullOrWhiteSpace(configured)) return (null, null);

        var match = organisations.FirstOrDefault(
            o => string.Equals(o.Name, configured, StringComparison.OrdinalIgnoreCase)
              || string.Equals(o.Id, configured, StringComparison.OrdinalIgnoreCase));

        if (match is not null) return (match.Id, null);

        var available = organisations.Count == 0
            ? Loc.T("Inject_Error_OrgNone")
            : string.Join(", ", organisations.Select(o => o.Name));

        return (null, Loc.F("Inject_Error_OrgMissing", configured, available));
    }

    // ───────────── Les appels ─────────────

    public async Task<SecretSourceFailure?> PreflightAsync(CancellationToken token)
    {
        var exe = Executable();
        if (exe is null) return new SecretSourceFailure(Loc.T("Inject_Error_CliMissing"));

        var status = await RunAsync(exe, ["status"], NoSecrets, token).ConfigureAwait(false);
        var failure = StatusFailure(BitwardenCli.ParseStatus(status.Stdout));

        return failure is null ? null : new SecretSourceFailure(failure, Diagnostic(status));
    }

    /// <summary>
    /// La date de dernière synchronisation du cache local de la CLI, sans mot de passe.
    /// </summary>
    /// <remarks>
    /// <c>bw status</c> n'a besoin d'aucune session : la fenêtre des Options peut donc afficher
    /// l'âge du cache sans jamais approcher un secret.
    /// </remarks>
    public async Task<DateTime?> LastRefreshAsync(CancellationToken token)
    {
        var exe = Executable();
        if (exe is null) return null;

        try
        {
            var status = await RunAsync(exe, ["status"], NoSecrets, token).ConfigureAwait(false);
            return BitwardenCli.ParseLastSync(status.Stdout);
        }
        catch (Exception ex)
        {
            LogService.Warn(ex, "Lecture de la date de synchronisation du coffre");
            return null;
        }
    }

    /// <summary>Déverrouille et synchronise le cache local de la CLI, à la demande.</summary>
    public async Task<SecretSourceFailure?> RefreshAsync(string credential, CancellationToken token)
    {
        var exe = Executable();
        if (exe is null) return new SecretSourceFailure(Loc.T("Inject_Error_CliMissing"));

        var environment = new Dictionary<string, string> { ["BW_PASSWORD"] = credential };

        var unlock = await RunAsync(exe, ["unlock", "--passwordenv", "BW_PASSWORD", "--raw"],
            environment, token).ConfigureAwait(false);

        if (!unlock.Ok || string.IsNullOrWhiteSpace(unlock.Stdout))
            return new SecretSourceFailure(Loc.T("Inject_Error_UnlockRefused"), Diagnostic(unlock));

        var session = new Dictionary<string, string> { ["BW_SESSION"] = unlock.Stdout.Trim() };

        var synced = await RunAsync(exe, ["sync"], session, token).ConfigureAwait(false);

        return synced.Ok ? null : new SecretSourceFailure(Loc.T("Inject_Error_CliFailed"), Diagnostic(synced));
    }

    /// <summary>
    /// Déverrouille, éventuellement synchronise, lit le coffre. La clé de session ne sort pas d'ici.
    /// </summary>
    /// <param name="refreshFirst">
    /// Rafraîchir le cache de la CLI avant de lire. Fait <b>ici</b>, entre le déverrouillage et la
    /// lecture : la clé de session existe déjà, donc c'est un appel de plus dans la séquence, sans
    /// second mot de passe ni second déverrouillage.
    /// </param>
    public async Task<SecretSourceOpening> OpenAsync(
        string credential, bool refreshFirst, CancellationToken token)
    {
        var exe = Executable();
        if (exe is null)
            return new SecretSourceOpening(null, new SecretSourceFailure(Loc.T("Inject_Error_CliMissing")));

        // Le mot de passe n'existe que dans l'environnement du processus enfant : jamais dans une
        // ligne de commande, jamais dans celui de DockPad.
        var environment = new Dictionary<string, string> { ["BW_PASSWORD"] = credential };

        var unlock = await RunAsync(exe, ["unlock", "--passwordenv", "BW_PASSWORD", "--raw"],
            environment, token).ConfigureAwait(false);

        if (!unlock.Ok || string.IsNullOrWhiteSpace(unlock.Stdout))
            return new SecretSourceOpening(null,
                new SecretSourceFailure(Loc.T("Inject_Error_UnlockRefused"), Diagnostic(unlock)));

        // La clé de session suit le même chemin, et ne quitte pas cette méthode.
        var session = new Dictionary<string, string> { ["BW_SESSION"] = unlock.Stdout.Trim() };

        string? warning = null;

        if (refreshFirst)
        {
            var synced = await RunAsync(exe, ["sync"], session, token).ConfigureAwait(false);
            if (!synced.Ok)
            {
                LogService.Warn(new InvalidOperationException(Diagnostic(synced) ?? "sync failed"), "Synchronisation du coffre avant injection");
                warning = Loc.T("Inject_Missing_SyncFailed");
            }
        }

        var configured = AppSettingsService.Current.VaultOrganization;
        string? organisationId = null;

        if (!string.IsNullOrWhiteSpace(configured))
        {
            var listed = await RunAsync(exe, ["list", "organizations"], session, token).ConfigureAwait(false);
            if (!listed.Ok)
                return new SecretSourceOpening(null,
                    new SecretSourceFailure(Loc.T("Inject_Error_CliFailed"), Diagnostic(listed)));

            var (id, orgFailure) = ResolveOrganisation(BitwardenCli.ParseOrganizations(listed.Stdout), configured);
            if (orgFailure is not null)
                return new SecretSourceOpening(null, new SecretSourceFailure(orgFailure));

            organisationId = id;
        }

        string[] arguments = organisationId is null
            ? ["list", "items"]
            : ["list", "items", "--organizationid", organisationId];

        var items = await RunAsync(exe, arguments, session, token).ConfigureAwait(false);
        if (!items.Ok)
            return new SecretSourceOpening(null,
                new SecretSourceFailure(Loc.T("Inject_Error_CliFailed"), Diagnostic(items)));

        var vault = new SecretVault(BitwardenCli.ParseItems(items.Stdout), configured);

        return new SecretSourceOpening(vault.Lookup, null, warning);
    }

    // ───────────── Détails ─────────────

    private static readonly Dictionary<string, string> NoSecrets = [];

    private static string? Executable() =>
        BitwardenCli.Locate(AppSettingsService.Current.BitwardenCliPath);

    private static Task<CliResult> RunAsync(string exe, string[] arguments,
        IReadOnlyDictionary<string, string> secrets, CancellationToken token) =>
        BitwardenCli.RunAsync(exe, arguments, secrets, token);

    /// <summary>
    /// Le diagnostic à montrer en infobulle et à journaliser : l'erreur standard et le code de
    /// sortie, jamais la sortie standard — celle-là porte le coffre.
    /// </summary>
    private static string? Diagnostic(CliResult result)
    {
        var stderr = result.Stderr.Trim();
        var first = stderr.Split('\n').FirstOrDefault()?.Trim();
        return string.IsNullOrEmpty(first) ? $"exit {result.ExitCode}" : $"exit {result.ExitCode} — {first}";
    }
}
