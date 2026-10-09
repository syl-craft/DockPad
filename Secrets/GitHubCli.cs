using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace DockPad.Secrets;

/// <summary>
/// Le seul point qui parle à <c>gh.exe</c>, calqué sur <see cref="BitwardenCli"/>.
/// </summary>
/// <remarks>
/// <para>
/// <b>La valeur passe par l'entrée standard, jamais en argument.</b> <c>gh secret set</c> et
/// <c>gh variable set</c> la lisent sur stdin quand aucun corps n'est donné ; une ligne de commande,
/// elle, est lisible de tout processus de la machine — DockPad lui-même en lit par WMI pour
/// <c>SwitchToProcess</c>. Une garde de test interdit le drapeau de corps dans tout le dossier.
/// </para>
/// <para>
/// <b>L'entrée standard est en UTF-8 sans BOM.</b> Par défaut .NET l'écrit dans la page de code de
/// la console : un secret accentué arriverait abîmé sur GitHub, et rien ne le dirait. Le BOM, lui,
/// deviendrait les trois premiers octets du secret.
/// </para>
/// <para>
/// Un second lanceur plutôt qu'un lanceur partagé avec Bitwarden : celui-là porte l'environnement
/// secret de <c>bw</c> et son encodage de fiche, et le toucher pour <c>gh</c> ferait relire toute la
/// chaîne d'audit de la CLI Bitwarden. Les deux restent courts et se lisent côte à côte.
/// </para>
/// <para>
/// La sortie standard ne porte ici que des noms et des dates ; elle n'est pas journalisée pour
/// autant, par la même règle de flux que pour <c>bw</c>.
/// </para>
/// </remarks>
public static class GitHubCli
{
    /// <summary>Au-delà, on rend la main : un GitHub injoignable ne doit pas figer la fenêtre.</summary>
    public const int TimeoutSeconds = 60;

    private static readonly Encoding Utf8WithoutBom = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);

    // ───────────── Localisation ─────────────

    /// <summary>
    /// Le chemin de <c>gh.exe</c> : celui qui est réglé s'il existe, sinon le <c>PATH</c>, sinon le
    /// dossier d'installation par défaut.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Un chemin réglé qui n'existe plus retombe sur la détection, comme pour <c>bw</c> : une
    /// désinstallation ou un déplacement ne doit pas rendre la fonctionnalité muette.
    /// </para>
    /// <para>
    /// Le dossier d'installation est cherché <b>même quand le <c>PATH</c> échoue</b> : celui d'un
    /// processus est figé à son démarrage, et DockPad tourne des semaines — un <c>gh</c> installé
    /// entre-temps n'y apparaîtrait pas.
    /// </para>
    /// </remarks>
    public static string? FindExecutable(string configured, string pathVariable, string programFiles)
    {
        if (!string.IsNullOrWhiteSpace(configured) && File.Exists(configured)) return configured;

        foreach (var dir in pathVariable.Split(';', StringSplitOptions.RemoveEmptyEntries))
        {
            try
            {
                var candidate = Path.Combine(dir.Trim(), "gh.exe");
                if (File.Exists(candidate)) return candidate;
            }
            catch (ArgumentException)
            {
                // Le PATH d'une machine réelle porte des entrées mortes et des caractères illégaux.
            }
        }

        if (string.IsNullOrWhiteSpace(programFiles)) return null;

        var installed = Path.Combine(programFiles, "GitHub CLI", "gh.exe");
        return File.Exists(installed) ? installed : null;
    }

    /// <summary>Le chemin résolu depuis le réglage, ou <c>null</c> si <c>gh</c> est introuvable.</summary>
    /// <param name="configured">Le chemin réglé dans les Options ; vide = détection seule.</param>
    public static string? Locate(string configured) => FindExecutable(
        configured,
        Environment.GetEnvironmentVariable("PATH") ?? "",
        Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles));

    // ───────────── Arguments ─────────────

    public static IReadOnlyList<string> ListArguments(GitHubTargetKind kind, string repo, string? environment) =>
        [Noun(kind), "list", .. Target(repo, environment), "--json", "name,updatedAt"];

    /// <summary>La cible et le nom, rien d'autre : la valeur suit par l'entrée standard.</summary>
    public static IReadOnlyList<string> SetArguments(GitHubTargetKind kind, string name, string repo, string? environment) =>
        [Noun(kind), "set", name, .. Target(repo, environment)];

    private static string Noun(GitHubTargetKind kind) =>
        kind == GitHubTargetKind.Secrets ? "secret" : "variable";

    private static IEnumerable<string> Target(string repo, string? environment) =>
        environment == null ? ["--repo", repo] : ["--repo", repo, "--env", environment];

    // ───────────── Décodage ─────────────

    /// <summary>
    /// Ce que <c>gh … list --json name,updatedAt</c> rend, ou <c>null</c> si la sortie est illisible.
    /// </summary>
    /// <remarks>
    /// <c>null</c> et non une liste vide : une sortie illisible lue comme « rien sur GitHub »
    /// annoncerait chaque nom à créer et aucun en trop — une vérification réussie qui n'a rien vérifié.
    /// </remarks>
    public static IReadOnlyList<GitHubRemoteEntry>? ParseList(string stdout)
    {
        var start = stdout.IndexOf('[');
        if (start < 0) return null;

        try
        {
            using var doc = JsonDocument.Parse(stdout[start..]);

            return doc.RootElement.EnumerateArray()
                .Where(e => e.TryGetProperty("name", out var name) && name.ValueKind == JsonValueKind.String)
                .Select(e => new GitHubRemoteEntry(
                    e.GetProperty("name").GetString() ?? "",
                    e.TryGetProperty("updatedAt", out var updated)
                        && updated.ValueKind == JsonValueKind.String
                        && DateTimeOffset.TryParse(updated.GetString(),
                            System.Globalization.CultureInfo.InvariantCulture,
                            System.Globalization.DateTimeStyles.AssumeUniversal, out var parsed)
                        ? parsed
                        : null))
            .ToList();
        }
        catch (JsonException) { return null; }
        catch (InvalidOperationException) { return null; }
    }

    // ───────────── Exécution ─────────────

    /// <summary>
    /// Lance <c>gh</c>. <paramref name="stdin"/> est le <b>seul</b> chemin par lequel une valeur
    /// atteint le processus.
    /// </summary>
    /// <remarks>
    /// <c>GH_PROMPT_DISABLED</c> : sans terminal, une question interactive bloquerait jusqu'au délai.
    /// </remarks>
    /// <remarks>
    /// <b>Les modes debug hérités sont retirés.</b> <c>GH_DEBUG=api</c> fait écrire à <c>gh</c> le
    /// corps de ses requêtes HTTP sur l'erreur standard — donc la valeur d'une variable envoyée — et
    /// l'erreur standard est un diagnostic qui finit au journal.
    /// </remarks>
    public static async Task<CliResult> RunAsync(
        string exe, IReadOnlyList<string> arguments, CancellationToken token, string? stdin = null)
    {
        var psi = new ProcessStartInfo(exe)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardInput = stdin != null,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardInputEncoding = stdin != null ? Utf8WithoutBom : null,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
        };

        foreach (var argument in arguments) psi.ArgumentList.Add(argument);
        psi.Environment["GH_PROMPT_DISABLED"] = "1";
        psi.Environment["GH_NO_UPDATE_NOTIFIER"] = "1";
        psi.Environment["NO_COLOR"] = "1";
        psi.Environment.Remove("GH_DEBUG");
        psi.Environment.Remove("DEBUG");

        using var process = Process.Start(psi)
            ?? throw new InvalidOperationException("The GitHub CLI could not be started.");

        var stdout = process.StandardOutput.ReadToEndAsync(token);
        var stderr = process.StandardError.ReadToEndAsync(token);

        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(token);
        deadline.CancelAfter(TimeSpan.FromSeconds(TimeoutSeconds));

        // Même règle que pour bw : une annulation pendant l'écriture de la valeur doit tuer gh, pas
        // le laisser attendre la fin d'une entrée qui ne viendra plus.
        try
        {
            if (stdin != null)
            {
                await process.StandardInput.WriteAsync(stdin.AsMemory(), deadline.Token).ConfigureAwait(false);
                process.StandardInput.Close();
            }

            await process.WaitForExitAsync(deadline.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            try { process.Kill(entireProcessTree: true); } catch (Exception ex) { Services.LogService.Warn(ex, "Arrêt de gh.exe"); }
            throw;
        }

        return new CliResult(process.ExitCode,
            await stdout.ConfigureAwait(false),
            await stderr.ConfigureAwait(false));
    }
}
