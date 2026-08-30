using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace DockPad.Secrets;

/// <summary>Une organisation du coffre, telle que <c>bw list organizations</c> la rend.</summary>
public sealed class BwOrganization
{
    public string Id { get; set; } = "";

    public string Name { get; set; } = "";
}

/// <summary>Ce qu'un appel à la CLI a produit.</summary>
public sealed record CliResult(int ExitCode, string Stdout, string Stderr)
{
    public bool Ok => ExitCode == 0;
}

/// <summary>
/// Le seul point qui parle à <c>bw.exe</c>.
/// </summary>
/// <remarks>
/// <para>
/// <b>Ni le mot de passe maître ni la clé de session ne passent par la ligne de commande.</b> Ils
/// vont dans <c>ProcessStartInfo.Environment</c>, qui n'appartient qu'au processus enfant : une
/// ligne de commande est lisible par tout autre processus de la machine — DockPad lui-même en lit
/// par WMI pour <c>SwitchToProcess</c>. Le script PowerShell d'origine passait
/// <c>--session $env:BW_SESSION</c> en argument, et posait <c>BW_PASSWORD</c> sur son propre
/// shell ; les deux sont corrigés ici, et une garde de test empêche la régression.
/// </para>
/// <para>
/// <c>UseShellExecute = false</c> et <c>ArgumentList</c> : aucun <c>cmd</c>, aucun
/// <c>powershell</c>, donc rien à échapper et aucune fenêtre console qui clignote.
/// </para>
/// <para>
/// <b>La sortie standard ne va jamais au journal</b> : elle porte les données du coffre. Seuls
/// l'erreur standard et le code de sortie sont des diagnostics.
/// </para>
/// </remarks>
public static class BitwardenCli
{
    /// <summary>Au-delà, on rend la main : un Vaultwarden injoignable ne doit pas figer la fenêtre.</summary>
    public const int TimeoutSeconds = 60;

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    // ───────────── Localisation ─────────────

    /// <summary>
    /// Le chemin de <c>bw.exe</c> : celui qui est réglé s'il existe, sinon le <c>PATH</c>, sinon
    /// l'arborescence WinGet.
    /// </summary>
    /// <remarks>
    /// <b>Un chemin réglé qui n'existe plus retombe sur la détection</b>, et ce n'est pas de la
    /// complaisance : le dossier d'installation WinGet porte un identifiant de version, donc un
    /// chemin enregistré devient faux à la première mise à jour de la CLI. Le cas se produira.
    /// </remarks>
    public static string? FindExecutable(string configured, string pathVariable, string wingetRoot)
    {
        if (!string.IsNullOrWhiteSpace(configured) && File.Exists(configured)) return configured;

        foreach (var dir in pathVariable.Split(';', StringSplitOptions.RemoveEmptyEntries))
        {
            try
            {
                var candidate = Path.Combine(dir.Trim(), "bw.exe");
                if (File.Exists(candidate)) return candidate;
            }
            catch (ArgumentException)
            {
                // Le PATH d'une machine réelle porte des entrées mortes et des caractères illégaux.
            }
        }

        if (string.IsNullOrWhiteSpace(wingetRoot) || !Directory.Exists(wingetRoot)) return null;

        try
        {
            return Directory
                .EnumerateFiles(wingetRoot, "bw.exe", SearchOption.AllDirectories)
                .FirstOrDefault();
        }
        catch (Exception ex)
        {
            Services.LogService.Warn(ex, "Recherche de bw.exe sous WinGet");
            return null;
        }
    }

    /// <summary>Le chemin résolu depuis les réglages, ou <c>null</c> si la CLI est introuvable.</summary>
    public static string? Locate(string configured) => FindExecutable(
        configured,
        Environment.GetEnvironmentVariable("PATH") ?? "",
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            @"Microsoft\WinGet\Packages"));

    // ───────────── Décodage ─────────────

    /// <summary>Le statut rendu par <c>bw status</c>, ou <c>null</c> si la sortie est illisible.</summary>
    /// <remarks>
    /// On repart du premier <c>{</c> : la CLI préfixe parfois sa sortie d'un avertissement de mise
    /// à jour, qui ferait échouer une désérialisation appliquée à la ligne entière.
    /// </remarks>
    public static string? ParseStatus(string stdout)
    {
        var json = FromFirstBrace(stdout, '{');
        if (json is null) return null;

        try
        {
            using var doc = JsonDocument.Parse(json);
            return doc.RootElement.TryGetProperty("status", out var status) ? status.GetString() : null;
        }
        catch (JsonException) { return null; }
    }

    /// <summary>
    /// La date de dernière synchronisation, ou <c>null</c> si le coffre n'en a jamais eu.
    /// </summary>
    /// <remarks>
    /// La CLI travaille sur un <b>cache local</b> : cette date dit si ce qu'on s'apprête à lire est
    /// à jour. Un item ajouté au coffre après elle n'existe pas encore pour <c>bw list items</c> —
    /// c'est la confusion la plus coûteuse de cette fonctionnalité, et l'afficher la désamorce.
    /// </remarks>
    public static DateTime? ParseLastSync(string stdout)
    {
        var json = FromFirstBrace(stdout, '{');
        if (json is null) return null;

        try
        {
            using var doc = JsonDocument.Parse(json);
            return doc.RootElement.TryGetProperty("lastSync", out var last)
                && last.ValueKind == JsonValueKind.String
                && DateTime.TryParse(last.GetString(), System.Globalization.CultureInfo.InvariantCulture,
                    System.Globalization.DateTimeStyles.AdjustToUniversal | System.Globalization.DateTimeStyles.AssumeUniversal,
                    out var parsed)
                ? parsed
                : null;
        }
        catch (JsonException) { return null; }
    }

    public static IReadOnlyList<BwItem> ParseItems(string stdout) => ParseArray<BwItem>(stdout);

    public static IReadOnlyList<BwOrganization> ParseOrganizations(string stdout) =>
        ParseArray<BwOrganization>(stdout);

    private static IReadOnlyList<T> ParseArray<T>(string stdout)
    {
        var json = FromFirstBrace(stdout, '[');
        if (json is null) return [];

        try { return JsonSerializer.Deserialize<List<T>>(json, JsonOpts) ?? []; }
        catch (JsonException) { return []; }
    }

    private static string? FromFirstBrace(string stdout, char brace)
    {
        var start = stdout.IndexOf(brace);
        return start < 0 ? null : stdout[start..];
    }

    // ───────────── Exécution ─────────────

    /// <summary>
    /// Lance la CLI. <paramref name="secretEnvironment"/> est le <b>seul</b> chemin par lequel un
    /// mot de passe ou une clé de session atteint le processus.
    /// </summary>
    public static async Task<CliResult> RunAsync(
        string exe,
        IReadOnlyList<string> arguments,
        IReadOnlyDictionary<string, string> secretEnvironment,
        CancellationToken token)
    {
        var psi = new ProcessStartInfo(exe)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
        };

        foreach (var argument in arguments) psi.ArgumentList.Add(argument);
        foreach (var (name, value) in secretEnvironment) psi.Environment[name] = value;

        using var process = Process.Start(psi)
            ?? throw new InvalidOperationException("The Bitwarden CLI could not be started.");

        // Lecture asynchrone des deux flux : `bw list items` remplit largement le tampon d'un pipe,
        // et attendre la fin du processus avant de lire l'interbloquerait.
        var stdout = process.StandardOutput.ReadToEndAsync(token);
        var stderr = process.StandardError.ReadToEndAsync(token);

        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(token);
        deadline.CancelAfter(TimeSpan.FromSeconds(TimeoutSeconds));

        try
        {
            await process.WaitForExitAsync(deadline.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            try { process.Kill(entireProcessTree: true); } catch (Exception ex) { Services.LogService.Warn(ex, "Arrêt de bw.exe"); }
            throw;
        }

        return new CliResult(process.ExitCode,
            await stdout.ConfigureAwait(false),
            await stderr.ConfigureAwait(false));
    }
}
