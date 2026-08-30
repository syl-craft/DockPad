using System.IO;
using System.Threading;
using System.Threading.Tasks;
using DockPad.Services;

namespace DockPad.Secrets;

/// <summary>Ce que le mode fichiers a produit : les noms écrits, où, et combien d'items lus.</summary>
/// <param name="ItemCount">
/// Items de coffre <b>distincts</b> consultés, et non le nombre de fichiers : les cinq secrets du
/// compose de référence viennent tous de <c>vaultwarden-infra</c>, et annoncer « 5 items lus »
/// donnerait une fausse idée de ce qui a été interrogé.
/// </param>
public sealed record SecretFilesOutcome(string Folder, IReadOnlyList<string> Written, int ItemCount);

/// <summary>
/// Ce qu'une étape a produit : <b>soit</b> un rendu, <b>soit</b> des fichiers, <b>soit</b> une
/// liste d'échecs — jamais deux à la fois.
/// </summary>
/// <remarks>
/// <c>Diagnostic</c> est l'erreur standard de la CLI — <b>jamais</b> sa sortie standard, qui porte
/// les données du coffre. Il n'est pas traduit : c'est un diagnostic, il va au journal et en
/// infobulle derrière une phrase, elle, traduite. Même règle que les causes d'indisponibilité du
/// quota dans le bandeau Usage.
/// </remarks>
public sealed record InjectionReport
{
    /// <summary>Ce qui a échoué. Vide <b>si et seulement si</b> quelque chose a été produit.</summary>
    public IReadOnlyList<string> Failures { get; private init; } = [];

    /// <summary>Le rendu, en mode presse-papier.</summary>
    public SecretRenderResult? Render { get; private init; }

    /// <summary>Les fichiers écrits, en mode fichiers.</summary>
    public SecretFilesOutcome? Files { get; private init; }

    public string? Diagnostic { get; private init; }

    public bool Ok => Failures.Count == 0;

    public static InjectionReport Fail(string failure, string? diagnostic = null) =>
        new() { Failures = [failure], Diagnostic = diagnostic };

    public static InjectionReport Failed(IReadOnlyList<string> failures, string? diagnostic = null) =>
        new() { Failures = failures, Diagnostic = diagnostic };

    /// <summary>Le rendu presse-papier — un échec de rendu devient un échec de rapport, ici et nulle part ailleurs.</summary>
    public static InjectionReport Rendered(SecretRenderResult result) =>
        result.Ok ? new() { Render = result } : new() { Failures = result.Failures };

    public static InjectionReport Written(SecretFilesOutcome files) => new() { Files = files };

    /// <summary>Le cache local a été rafraîchi. Rien n'a été lu, rien n'a été produit.</summary>
    public static InjectionReport Synced() => new() { DidSync = true };

    /// <summary>Vrai quand l'opération était une synchronisation, et qu'elle a abouti.</summary>
    public bool DidSync { get; private init; }
}

/// <summary>
/// Enchaîne les appels à la CLI, du fichier au texte rendu.
/// </summary>
/// <remarks>
/// <para>
/// <b>Aucune clé de session n'est conservée.</b> Elle naît d'un <c>unlock</c>, vit le temps de
/// <see cref="RenderAsync"/>, et sort de portée avec la méthode. C'est le choix retenu : DockPad
/// démarre avec Windows et tourne des semaines, une clé de coffre n'a rien à y faire — au prix du
/// mot de passe maître à chaque injection.
/// </para>
/// <para>
/// <b>Quatre appels au plus</b>, dont un seul <c>list items</c> qui ramène tout. Le script d'origine
/// lançait une recherche par item ; ramener l'ensemble en un appel est plus rapide, et déplace la
/// résolution du côté testable de la frontière (voir <see cref="SecretVault"/>).
/// </para>
/// </remarks>
public static class SecretInjectionService
{
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

    // ───────────── Le fichier ─────────────

    /// <summary>
    /// Au-delà, on refuse sans lire.
    /// </summary>
    /// <remarks>
    /// L'entrée de menu vit sur <b>tous</b> les fichiers : un clic droit sur une image disque
    /// chargerait des giga-octets en mémoire et les passerait au parseur YAML. Un gabarit de
    /// déploiement pèse quelques dizaines de kilo-octets ; quatre méga-octets laissent large.
    /// </remarks>
    public const int MaxTemplateBytes = 4 * 1024 * 1024;

    /// <summary>Le contenu du gabarit, ou l'échec à afficher. Le fichier n'est jamais modifié.</summary>
    public static (string? Content, InjectionReport? Failure) ReadTemplate(string path)
    {
        if (!File.Exists(path))
            return (null, InjectionReport.Fail(Loc.F("Inject_Error_FileMissing", Path.GetFileName(path))));

        var size = new FileInfo(path).Length;
        if (size > MaxTemplateBytes)
            return (null, InjectionReport.Fail(
                Loc.F("Inject_Error_FileTooBig", MaxTemplateBytes / (1024 * 1024))));

        try { return (File.ReadAllText(path), null); }
        catch (Exception ex)
        {
            LogService.Warn(ex, "Lecture du gabarit à injecter");
            return (null, InjectionReport.Fail(Loc.T("Inject_Error_FileUnreadable"), ex.GetType().Name));
        }
    }

    // ───────────── Les appels ─────────────

    /// <summary>
    /// Vérifie que la CLI est là et que le coffre est utilisable. <c>null</c> = on peut demander le
    /// mot de passe.
    /// </summary>
    public static async Task<InjectionReport?> PreflightAsync(CancellationToken token)
    {
        var exe = Executable();
        if (exe is null) return InjectionReport.Fail(Loc.T("Inject_Error_CliMissing"));

        var status = await RunAsync(exe, ["status"], NoSecrets, token).ConfigureAwait(false);
        var failure = StatusFailure(BitwardenCli.ParseStatus(status.Stdout));

        return failure is null ? null : InjectionReport.Fail(failure, Diagnostic(status));
    }

    /// <summary>
    /// La date de dernière synchronisation du cache local de la CLI, sans mot de passe.
    /// </summary>
    /// <remarks>
    /// <c>bw status</c> n'a besoin d'aucune session : la fenêtre des Options peut donc afficher
    /// l'âge du cache sans jamais approcher un secret.
    /// </remarks>
    public static async Task<DateTime?> LastSyncAsync(CancellationToken token)
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

    /// <summary>Déverrouille et synchronise le cache local de la CLI.</summary>
    /// <remarks>
    /// Déclenché à la main depuis les Options, jamais avant chaque injection : synchroniser à
    /// chaque clic droit paierait un aller-retour réseau pour un coffre qui bouge rarement.
    /// </remarks>
    public static async Task<InjectionReport> SyncAsync(string masterPassword, CancellationToken token)
    {
        var exe = Executable();
        if (exe is null) return InjectionReport.Fail(Loc.T("Inject_Error_CliMissing"));

        var environment = new Dictionary<string, string> { ["BW_PASSWORD"] = masterPassword };

        var unlock = await RunAsync(exe, ["unlock", "--passwordenv", "BW_PASSWORD", "--raw"],
            environment, token).ConfigureAwait(false);

        if (!unlock.Ok || string.IsNullOrWhiteSpace(unlock.Stdout))
            return InjectionReport.Fail(Loc.T("Inject_Error_UnlockRefused"), Diagnostic(unlock));

        var session = new Dictionary<string, string> { ["BW_SESSION"] = unlock.Stdout.Trim() };

        var synced = await RunAsync(exe, ["sync"], session, token).ConfigureAwait(false);

        return synced.Ok
            ? InjectionReport.Synced()
            : InjectionReport.Fail(Loc.T("Inject_Error_CliFailed"), Diagnostic(synced));
    }

    /// <summary>Déverrouille, lit le coffre, rend le gabarit dans le presse-papier.</summary>
    public static async Task<InjectionReport> RenderAsync(
        string content, string masterPassword, CancellationToken token)
    {
        var (vault, failure) = await OpenVaultAsync(masterPassword, token).ConfigureAwait(false);
        return failure ?? InjectionReport.Rendered(SecretTemplate.Render(content, vault!.Lookup));
    }

    /// <summary>
    /// Déverrouille, lit le coffre, écrit les fichiers de secrets annotés à côté du compose.
    /// </summary>
    /// <remarks>
    /// <b>Tout ou rien, comme le rendu</b> : chaque annotation est résolue d'abord, et un seul
    /// échec annule l'écriture entière. Un jeu de secrets déjà correct sur le disque n'est jamais
    /// remplacé à moitié.
    /// </remarks>
    public static async Task<InjectionReport> WriteFilesAsync(
        string content, string folder, string masterPassword, CancellationToken token)
    {
        var (entries, annotationFailures, yamlError) = ComposeSecrets.Extract(content);

        if (annotationFailures.Count > 0)
            return InjectionReport.Failed(annotationFailures, yamlError);

        // Refuser AVANT d'ouvrir le coffre : deux annotations qui visent le même fichier, ou un
        // nom qui sortirait du dossier, ne produiront rien de bon — inutile de réclamer un mot de
        // passe maître pour s'en apercevoir après.
        var blocking = SecretFileWriter.Conflicts(entries)
            .Concat(entries.Where(e => !SecretFileWriter.IsWritableName(e.FileName))
                           .Select(e => Loc.F("Inject_Error_BadFileName", e.Key)))
            .ToList();

        if (blocking.Count > 0) return InjectionReport.Failed(blocking);

        var (vault, failure) = await OpenVaultAsync(masterPassword, token).ConfigureAwait(false);
        if (failure is not null) return failure;

        var files = new List<SecretFile>();
        var unresolved = new List<string>();

        foreach (var entry in entries)
        {
            var found = vault!.Lookup(entry.Marker);
            if (string.IsNullOrEmpty(found.Value))
                unresolved.Add(found.Failure ?? Loc.F("Inject_Error_EmptyField", entry.Marker.Item, entry.Marker.Field));
            else
                files.Add(new SecretFile(entry.FileName, found.Value));
        }

        if (unresolved.Count > 0)
            return InjectionReport.Failed(unresolved.Distinct(StringComparer.Ordinal).ToList());

        var written = SecretFileWriter.Write(folder, files);
        var target = System.IO.Path.Combine(folder, SecretFileWriter.FolderName);

        var items = entries.Select(e => e.Marker.Item).Distinct(StringComparer.Ordinal).Count();

        return InjectionReport.Written(new SecretFilesOutcome(target, written, items));
    }

    /// <summary>Le coffre ouvert, ou l'échec à afficher. La clé de session ne sort pas d'ici.</summary>
    private static async Task<(SecretVault? Vault, InjectionReport? Failure)> OpenVaultAsync(
        string masterPassword, CancellationToken token)
    {
        var exe = Executable();
        if (exe is null) return (null, InjectionReport.Fail(Loc.T("Inject_Error_CliMissing")));

        // Le mot de passe n'existe que dans l'environnement du processus enfant : jamais dans une
        // ligne de commande, jamais dans celui de DockPad.
        var environment = new Dictionary<string, string> { ["BW_PASSWORD"] = masterPassword };

        var unlock = await RunAsync(exe, ["unlock", "--passwordenv", "BW_PASSWORD", "--raw"],
            environment, token).ConfigureAwait(false);

        if (!unlock.Ok || string.IsNullOrWhiteSpace(unlock.Stdout))
            return (null, InjectionReport.Fail(Loc.T("Inject_Error_UnlockRefused"), Diagnostic(unlock)));

        // La clé de session suit le même chemin, et ne quitte pas cette méthode.
        var session = new Dictionary<string, string> { ["BW_SESSION"] = unlock.Stdout.Trim() };

        var configured = AppSettingsService.Current.VaultOrganization;
        string? organisationId = null;

        if (!string.IsNullOrWhiteSpace(configured))
        {
            var listed = await RunAsync(exe, ["list", "organizations"], session, token).ConfigureAwait(false);
            if (!listed.Ok) return (null, InjectionReport.Fail(Loc.T("Inject_Error_CliFailed"), Diagnostic(listed)));

            var (id, orgFailure) = ResolveOrganisation(BitwardenCli.ParseOrganizations(listed.Stdout), configured);
            if (orgFailure is not null) return (null, InjectionReport.Fail(orgFailure));
            organisationId = id;
        }

        string[] arguments = organisationId is null
            ? ["list", "items"]
            : ["list", "items", "--organizationid", organisationId];

        var items = await RunAsync(exe, arguments, session, token).ConfigureAwait(false);
        if (!items.Ok) return (null, InjectionReport.Fail(Loc.T("Inject_Error_CliFailed"), Diagnostic(items)));

        return (new SecretVault(BitwardenCli.ParseItems(items.Stdout), configured), null);
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
