using System.Globalization;
using System.IO;
using System.Text;
using DockPad.Secrets;
using DockPad.Services.Localization;

namespace DockPad.Tests.Secrets;

/// <summary>
/// L'aiguillage entre les deux modes, et l'écriture des fichiers de secrets.
/// </summary>
public class SecretFilesTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "dockpad-files-" + Guid.NewGuid().ToString("N"));

    public SecretFilesTests()
    {
        Loc.SetCulture(CultureInfo.GetCultureInfo("fr"));
        Directory.CreateDirectory(_dir);
    }

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch (IOException) { }
        GC.SuppressFinalize(this);
    }

    private const string Annotated = """
        secrets:
          vw-token:
            file: /share/CACHEDEV1_DATA/vaultwarden/secrets/token
            x-bw:
              item: infra
              field: token
        """;

    // ───────────── L'aiguillage ─────────────

    [Fact]
    public void DesMarqueurs_DonnentLeModePressePapier()
    {
        Assert.Equal(SecretMode.Clipboard, SecretPlan.Of("token: \"{{ bw:ntfy:token }}\""));
    }

    [Fact]
    public void DesAnnotations_DonnentLeModeFichiers()
    {
        Assert.Equal(SecretMode.Files, SecretPlan.Of(Annotated));
    }

    [Fact]
    public void LesDeuxAlaFois_SontUnRefus()
    {
        // Deviner serait pire que demander : les deux modes produisent des choses différentes, à
        // des endroits différents.
        Assert.Equal(SecretMode.Ambiguous, SecretPlan.Of(Annotated + "\n# {{ bw:a:b }}\nx: \"{{ bw:a:b }}\""));
    }

    [Fact]
    public void NiLUnNiLAutre_NEstPasUnMode()
    {
        Assert.Equal(SecretMode.None, SecretPlan.Of("image: nginx"));
    }

    [Fact]
    public void UnFichierQuiNEstPasDuYaml_MaisAvecDesMarqueurs_ResteEnPressePapier()
    {
        // Un .env porte des marqueurs et n'est pas du YAML. Faire trancher le parseur d'abord
        // basculerait ce cas courant vers un message d'erreur YAML sans rapport.
        Assert.Equal(SecretMode.Clipboard, SecretPlan.Of("TOKEN={{ bw:ntfy:token }}\nA=[b"));
    }

    [Fact]
    public void UneAnnotationIncomplete_CompteQuandMemeCommeModeFichiers()
    {
        // Sinon un compose dont une annotation est fautive répondrait « rien à rendre », au lieu de
        // nommer l'annotation à corriger.
        var mode = SecretPlan.Of("""
            secrets:
              vw-token:
                x-bw:
                  item: infra
                  field: token
            """);

        Assert.Equal(SecretMode.Files, mode);
    }

    // ───────────── L'écriture ─────────────

    [Fact]
    public void EcritLaValeurSansSautDeLigneFinal()
    {
        // containerboot lit TS_AUTHKEY via `file:` et ne rogne pas le contenu : un \n final
        // ferait échouer l'authentification Tailscale.
        SecretFileWriter.Write(_dir, [new SecretFile("token", "tk_42")]);

        var bytes = File.ReadAllBytes(Path.Combine(_dir, "secrets", "token"));

        Assert.Equal(Encoding.UTF8.GetBytes("tk_42"), bytes);
    }

    [Fact]
    public void EcritSansBom()
    {
        SecretFileWriter.Write(_dir, [new SecretFile("token", "tk")]);

        var bytes = File.ReadAllBytes(Path.Combine(_dir, "secrets", "token"));

        Assert.NotEqual(0xEF, bytes[0]);
    }

    [Fact]
    public void EcritDansUnSousDossierSecrets()
    {
        SecretFileWriter.Write(_dir, [new SecretFile("a", "1"), new SecretFile("b", "2")]);

        Assert.Equal("1", File.ReadAllText(Path.Combine(_dir, "secrets", "a")));
        Assert.Equal("2", File.ReadAllText(Path.Combine(_dir, "secrets", "b")));
    }

    [Fact]
    public void RemplaceUnFichierExistant()
    {
        SecretFileWriter.Write(_dir, [new SecretFile("token", "ancien")]);
        SecretFileWriter.Write(_dir, [new SecretFile("token", "nouveau")]);

        Assert.Equal("nouveau", File.ReadAllText(Path.Combine(_dir, "secrets", "token")));
    }

    [Fact]
    public void NeLaissePasDeFichierTemporaire()
    {
        SecretFileWriter.Write(_dir, [new SecretFile("token", "tk")]);

        var produced = Directory.GetFiles(Path.Combine(_dir, "secrets")).Select(Path.GetFileName).ToList();

        Assert.DoesNotContain(produced, f => f!.Contains("tmp", StringComparison.OrdinalIgnoreCase));
        Assert.Contains("token", produced);
    }

    [Fact]
    public void LeDossierSIgnoreLuiMeme()
    {
        // Les fichiers produits n'ont pas d'extension : `*.key` du .gitignore du dépôt ne les
        // couvre pas, et ils partiraient au premier `git add .`. Le dossier s'ignore donc seul,
        // qu'on soit dans un dépôt ou non.
        SecretFileWriter.Write(_dir, [new SecretFile("token", "tk")]);

        var ignore = File.ReadAllText(Path.Combine(_dir, "secrets", ".gitignore"));

        Assert.Contains("*", ignore, StringComparison.Ordinal);
        Assert.Contains("!.gitignore", ignore, StringComparison.Ordinal);
    }

    [Fact]
    public void LeGitignoreNEstPasCompteCommeUnSecretEcrit()
    {
        var written = SecretFileWriter.Write(_dir, [new SecretFile("token", "tk")]);

        Assert.Equal(["token"], written);
    }

    // ───────────── Ce que la revue a trouvé ─────────────

    [Fact]
    public void DeuxSecretsAuMemeNomDeFichier_SontUnRefus()
    {
        // Trouvaille de revue, gravité haute. Le temporaire se déduit du seul nom de base : deux
        // `file:` de dossiers différents mais de même nom écrasaient le même temporaire, et il en
        // sortait UN fichier portant la MAUVAISE valeur, l'autre absent. La garantie « rien ou
        // tout » était rompue, en silence.
        var doublons = ComposeSecrets.Extract("""
            secrets:
              a:
                file: /share/secrets/token
                x-bw: { item: infra, field: un }
              b:
                file: /ailleurs/token
                x-bw: { item: infra, field: deux }
            """);

        var conflits = SecretFileWriter.Conflicts(doublons.Entries);

        Assert.Equal([Loc.F("Inject_Error_DuplicateFile", "token")], conflits);
    }

    [Fact]
    public void DesNomsDeFichiersDistincts_NeSontPasUnConflit()
    {
        var ok = ComposeSecrets.Extract("""
            secrets:
              a:
                file: /share/secrets/un
                x-bw: { item: infra, field: un }
              b:
                file: /share/secrets/deux
                x-bw: { item: infra, field: deux }
            """);

        Assert.Empty(SecretFileWriter.Conflicts(ok.Entries));
    }

    [Fact]
    public void UnNomDeFichierVideOuDangereux_EstRefuse()
    {
        // `file: ./secrets/` donne un nom de base vide : le temporaire devenait
        // « <dossier>\secrets.dockpad-tmp », donc un secret en clair ÉCRIT HORS du dossier — et
        // hors du .gitignore que ce dossier pose pour lui-même.
        Assert.False(SecretFileWriter.IsWritableName(""));
        Assert.False(SecretFileWriter.IsWritableName(".."));
        Assert.False(SecretFileWriter.IsWritableName("sous/dossier"));
        Assert.False(SecretFileWriter.IsWritableName("a:b"));
        Assert.True(SecretFileWriter.IsWritableName("ts-authkey"));
    }

    [Fact]
    public void UneEcritureQuiEchoue_NeLaisseAucunTemporaireEnClair()
    {
        // Le second nom est illégal sous Windows : l'écriture lève à mi-parcours. Le premier
        // temporaire, lui, porte déjà un secret en clair — il doit disparaître.
        var files = new List<SecretFile> { new("bon", "s3cr3t"), new("in|valide", "autre") };

        Assert.ThrowsAny<Exception>(() => SecretFileWriter.Write(_dir, files));

        var restes = Directory.Exists(Path.Combine(_dir, "secrets"))
            ? Directory.GetFiles(Path.Combine(_dir, "secrets")).Select(Path.GetFileName).ToList()
            : [];

        Assert.DoesNotContain(restes, f => f!.Contains("tmp", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void UneDestinationTenueParUnAutreProcessus_NeLaissePasDeTemporaire()
    {
        // Reproduction DETERMINISTE de l'echec intermittent : on tient le fichier de destination
        // ouvert sans partage, comme le ferait un antivirus qui vient de le scanner, un indexeur,
        // ou un client de synchronisation. Le dossier « secrets » vit dans un depot git, et
        // souvent dans un dossier synchronise : le cas se produira.
        SecretFileWriter.Write(_dir, [new SecretFile("token", "ancien")]);
        var cible = Path.Combine(_dir, "secrets", "token");

        using (var _ = new FileStream(cible, FileMode.Open, FileAccess.Read, FileShare.None))
        {
            Assert.ThrowsAny<Exception>(() => SecretFileWriter.Write(_dir, [new SecretFile("token", "nouveau")]));
        }

        var restes = Directory.GetFiles(Path.Combine(_dir, "secrets")).Select(Path.GetFileName).ToList();
        Assert.DoesNotContain(restes, f => f!.Contains("tmp", StringComparison.OrdinalIgnoreCase));
        Assert.Equal("ancien", File.ReadAllText(cible));
    }

    [Fact]
    public async System.Threading.Tasks.Task UnVerrouPASSAGER_NEmpechePasLEcriture()
    {
        // C'est la cause de l'echec intermittent : un antivirus tient la destination quelques
        // millisecondes apres l'avoir scannee. La transformer en echec dur est exactement ce que
        // MSBuild evite en retentant -- on l'a vu retenter dix fois sur DockPad.exe.
        SecretFileWriter.Write(_dir, [new SecretFile("token", "ancien")]);
        var cible = Path.Combine(_dir, "secrets", "token");

        var verrou = new FileStream(cible, FileMode.Open, FileAccess.Read, FileShare.None);
        var relache = System.Threading.Tasks.Task.Run(() =>
        {
            System.Threading.Thread.Sleep(250);
            verrou.Dispose();
        });

        SecretFileWriter.Write(_dir, [new SecretFile("token", "nouveau")]);
        await relache;

        Assert.Equal("nouveau", File.ReadAllText(cible));
    }

    [Fact]
    public void UnVerrouSurLeSECONDFichier_NeLaissePasLePREMIERmisAJour()
    {
        // Trouvaille de revue. Mon test precedent ne couvrait qu'UN fichier : il prouvait le
        // tout-ou-rien de l'ecriture, pas celui de la bascule. Avec N secrets, un verrou sur le
        // k-ieme laissait les k-1 premiers porter les NOUVELLES valeurs et le reste les anciennes.
        // Un jeu de secrets a moitie a jour, c'est exactement ce que la classe jure impossible.
        SecretFileWriter.Write(_dir, [new SecretFile("un", "vieux-1"), new SecretFile("deux", "vieux-2")]);

        var second = Path.Combine(_dir, "secrets", "deux");

        using (var _ = new FileStream(second, FileMode.Open, FileAccess.Read, FileShare.None))
        {
            Assert.ThrowsAny<Exception>(() => SecretFileWriter.Write(
                _dir, [new SecretFile("un", "neuf-1"), new SecretFile("deux", "neuf-2")]));
        }

        // Rien n'a bouge : ni le premier, ni le second.
        Assert.Equal("vieux-1", File.ReadAllText(Path.Combine(_dir, "secrets", "un")));
        Assert.Equal("vieux-2", File.ReadAllText(second));
    }
}
