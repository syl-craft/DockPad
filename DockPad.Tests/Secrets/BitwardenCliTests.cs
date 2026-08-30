using System.IO;
using DockPad.Secrets;

namespace DockPad.Tests.Secrets;

/// <summary>
/// Localisation de <c>bw.exe</c> et décodage de ce que la CLI répond.
/// </summary>
/// <remarks>
/// Les deux parties de <c>BitwardenCli</c> qui se testent sans lancer un processus. Le lancement
/// lui-même n'est pas testé ici : c'est la garde « rien en ligne de commande » qui vérifie qu'il
/// passe par l'environnement.
/// </remarks>
public class BitwardenCliTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "dockpad-bw-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch (IOException) { }
        GC.SuppressFinalize(this);
    }

    private string Touch(params string[] segments)
    {
        var path = Path.Combine([_root, .. segments]);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, "");
        return path;
    }

    // ───────────── Localisation ─────────────

    [Fact]
    public void UnCheminConfigureQuiExiste_EstUtiliseTelQuel()
    {
        var configured = Touch("perso", "bw.exe");

        Assert.Equal(configured, BitwardenCli.FindExecutable(configured, pathVariable: "", wingetRoot: ""));
    }

    [Fact]
    public void UnCheminConfigureQuiNExistePlus_RetombeSurLaDetection()
    {
        // Le dossier WinGet porte un numéro de version : un chemin enregistré devient faux à la
        // première mise à jour de la CLI. Retomber sur la détection est le comportement qui
        // continue de marcher le jour où ça arrive.
        var detected = Touch("ailleurs", "bw.exe");

        var found = BitwardenCli.FindExecutable(
            Path.Combine(_root, "parti", "bw.exe"), "", Path.Combine(_root, "ailleurs"));

        Assert.Equal(detected, found);
    }

    [Fact]
    public void ChercheDansLeChemin()
    {
        var onPath = Touch("bin", "bw.exe");

        Assert.Equal(onPath, BitwardenCli.FindExecutable("", Path.Combine(_root, "bin"), ""));
    }

    [Fact]
    public void ChercheEnRecursifSousWinGet()
    {
        // winget installe sous un dossier versionné : Bitwarden.CLI_Microsoft.Winget.Source_xxx\
        var installed = Touch("WinGet", "Packages", "Bitwarden.CLI_abc123", "bw.exe");

        Assert.Equal(installed,
            BitwardenCli.FindExecutable("", "", Path.Combine(_root, "WinGet", "Packages")));
    }

    [Fact]
    public void LeCheminLEmporteSurWinGet()
    {
        var onPath = Touch("bin", "bw.exe");
        Touch("WinGet", "Packages", "Bitwarden.CLI_abc123", "bw.exe");

        Assert.Equal(onPath,
            BitwardenCli.FindExecutable("", Path.Combine(_root, "bin"),
                Path.Combine(_root, "WinGet", "Packages")));
    }

    [Fact]
    public void AucuneCli_NeDonneRien()
    {
        Assert.Null(BitwardenCli.FindExecutable("", Path.Combine(_root, "vide"), Path.Combine(_root, "vide")));
    }

    [Fact]
    public void UneEntreeDeCheminInvalide_NInterrompPasLaRecherche()
    {
        // Le PATH d'une machine réelle contient des entrées mortes et des caractères illégaux.
        var onPath = Touch("bin", "bw.exe");

        var found = BitwardenCli.FindExecutable("", "C:\\ne|marche|pas;" + Path.Combine(_root, "bin"), "");

        Assert.Equal(onPath, found);
    }

    // ───────────── Décodage ─────────────

    [Fact]
    public void LitLeStatutDuCoffre()
    {
        Assert.Equal("locked", BitwardenCli.ParseStatus("""
            {"serverUrl":"https://vault.example","userEmail":"x@y.z","status":"locked"}
            """));
    }

    [Fact]
    public void LitLeStatutMemeAvecDuBruitAvantLeJson()
    {
        // La CLI préfixe parfois sa sortie d'un avertissement de mise à jour.
        Assert.Equal("unauthenticated",
            BitwardenCli.ParseStatus("A newer version is available.\n{\"status\":\"unauthenticated\"}"));
    }

    [Fact]
    public void LitLesItemsAvecLeursChampsPersonnalises()
    {
        var items = BitwardenCli.ParseItems("""
            [{"name":"ntfy","notes":"une note",
              "login":{"username":"sylvain","password":"s3cr3t","totp":null},
              "fields":[{"name":"token","value":"tk_42"}]}]
            """);

        var item = Assert.Single(items);
        Assert.Equal("ntfy", item.Name);
        Assert.Equal("une note", item.Notes);
        Assert.Equal("s3cr3t", item.Login!.Password);
        Assert.Equal("tk_42", Assert.Single(item.Fields!).Value);
    }

    [Fact]
    public void LitUnItemSansLoginNiChamps()
    {
        // Une note sécurisée n'a ni login ni champs : le décodage ne doit pas s'en émouvoir.
        var items = BitwardenCli.ParseItems("""[{"name":"note seule","notes":"ceci"}]""");

        Assert.Null(Assert.Single(items).Login);
    }

    [Fact]
    public void LitLesOrganisationsParNomEtParIdentifiant()
    {
        var orgs = BitwardenCli.ParseOrganizations("""[{"id":"org-1","name":"NAS QNAP"}]""");

        var org = Assert.Single(orgs);
        Assert.Equal("org-1", org.Id);
        Assert.Equal("NAS QNAP", org.Name);
    }

    [Fact]
    public void UneSortieVide_DonneUneListeVide()
    {
        Assert.Empty(BitwardenCli.ParseItems(""));
        Assert.Empty(BitwardenCli.ParseOrganizations("   "));
    }

    [Fact]
    public void LitLaDateDeDerniereSynchro()
    {
        // La CLI travaille sur un cache local : cette date dit si ce qu'on va lire est à jour.
        var when = BitwardenCli.ParseLastSync("""
            {"status":"locked","lastSync":"2026-08-29T21:02:26.311Z"}
            """);

        Assert.Equal(new DateTime(2026, 8, 29, 21, 2, 26, 311, DateTimeKind.Utc), when!.Value.ToUniversalTime());
    }

    [Fact]
    public void UneDateDeSynchroAbsente_NEstPasUneErreur()
    {
        // Un coffre jamais synchronisé : il n'y a pas de date, et ce n'est pas une panne.
        Assert.Null(BitwardenCli.ParseLastSync("""{"status":"unauthenticated"}"""));
        Assert.Null(BitwardenCli.ParseLastSync("pas du json"));
    }
}
