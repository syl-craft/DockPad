using System.IO;
using DockPad.Secrets;

namespace DockPad.Tests.Secrets;

/// <summary>
/// Ce qui se vérifie de <c>gh</c> sans le lancer : arguments, lecture des listes, localisation.
/// </summary>
public class GitHubCliTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "dockpad-ghcli-" + Guid.NewGuid().ToString("N"));

    public GitHubCliTests() => Directory.CreateDirectory(_root);

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch (IOException) { }
    }

    [Fact]
    public void LaListeDesSecretsDUnEnvironnementDemandeNomEtDate()
    {
        Assert.Equal(
            ["secret", "list", "--repo", "a/b", "--env", "stores", "--json", "name,updatedAt"],
            GitHubCli.ListArguments(GitHubTargetKind.Secrets, "a/b", "stores"));
    }

    [Fact]
    public void SansEnvironnementLaListeVisLeDepot()
    {
        Assert.Equal(
            ["variable", "list", "--repo", "a/b", "--json", "name,updatedAt"],
            GitHubCli.ListArguments(GitHubTargetKind.Variables, "a/b", environment: null));
    }

    [Fact]
    public void LEnvoiNeNommeQueLaCibleJamaisLaValeur()
    {
        // La valeur part par l'entrée standard : `--body` la rendrait lisible de tout processus de
        // la machine, y compris par la lecture WMI que DockPad fait lui-même.
        var arguments = GitHubCli.SetArguments(GitHubTargetKind.Secrets, "EDGE_API_KEY", "a/b", "stores");

        Assert.Equal(["secret", "set", "EDGE_API_KEY", "--repo", "a/b", "--env", "stores"], arguments);
        Assert.DoesNotContain(arguments, a => a.StartsWith("--body", StringComparison.Ordinal) || a == "-b");
    }

    [Fact]
    public void LitLaListeRendueParGh()
    {
        const string stdout = """
            [{"name":"EDGE_API_KEY","updatedAt":"2026-09-01T10:00:00Z"},{"name":"X","updatedAt":null}]
            """;

        var entries = GitHubCli.ParseList(stdout) ?? throw new InvalidOperationException("liste attendue");

        Assert.Equal(["EDGE_API_KEY", "X"], entries.Select(e => e.Name));
        Assert.Equal(new DateTimeOffset(2026, 9, 1, 10, 0, 0, TimeSpan.Zero), entries[0].UpdatedAt);
        Assert.Null(entries[1].UpdatedAt);
    }

    [Fact]
    public void UneSortieIllisibleNEstPasUneListeVide()
    {
        // Lue comme vide, elle ferait annoncer chaque nom « à créer » : une vérification réussie
        // qui n'a rien vérifié.
        Assert.Null(GitHubCli.ParseList("pas du json"));
        Assert.Null(GitHubCli.ParseList("[{tronqué"));
    }

    [Fact]
    public void UneListeVideEstUneReponseValide()
    {
        Assert.Equal([], GitHubCli.ParseList("[]") ?? throw new InvalidOperationException("liste attendue"));
    }

    [Fact]
    public void TrouveGhDansLePath()
    {
        var bin = Directory.CreateDirectory(Path.Combine(_root, "bin")).FullName;
        var gh = Path.Combine(bin, "gh.exe");
        File.WriteAllText(gh, "");

        Assert.Equal(gh, GitHubCli.FindExecutable($@"C:\nulle-part;{bin}", programFiles: ""));
    }

    [Fact]
    public void RetombeSurLeDossierDInstallation()
    {
        var folder = Directory.CreateDirectory(Path.Combine(_root, "GitHub CLI")).FullName;
        var gh = Path.Combine(folder, "gh.exe");
        File.WriteAllText(gh, "");

        Assert.Equal(gh, GitHubCli.FindExecutable("", programFiles: _root));
    }

    [Fact]
    public void IntrouvableRendNull()
    {
        Assert.Null(GitHubCli.FindExecutable("", programFiles: _root));
    }
}
