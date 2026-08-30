using System.Globalization;
using System.IO;
using DockPad.Secrets;
using DockPad.Services.Localization;

namespace DockPad.Tests.Secrets;

/// <summary>
/// Le rendu partiel des fichiers de secrets : ce qu'on écrit, ce qu'on nomme, ce qu'on propose de
/// supprimer.
/// </summary>
/// <remarks>
/// <para>
/// Ces tests portent le renversement de la règle centrale. Une clé absente du coffre n'annule plus
/// tout : on produit ce qu'on peut et on nomme ce qui manque. Les deux moitiés dangereuses de ce
/// relâchement sont ici — <b>ne jamais écrire un fichier vide</b>, et <b>ne jamais supprimer sans
/// qu'on le demande</b>.
/// </para>
/// <para>
/// La langue est posée explicitement : sans quoi ces tests héritent de celle laissée par une autre
/// classe et passent ou cassent selon l'ordonnancement.
/// </para>
/// </remarks>
public class SecretBundleTests : IDisposable
{
    private readonly string _dir =
        Path.Combine(Path.GetTempPath(), "dockpad-bundle-" + Guid.NewGuid().ToString("N"));

    public SecretBundleTests()
    {
        Loc.SetCulture(CultureInfo.GetCultureInfo("fr"));
        Directory.CreateDirectory(_dir);
    }

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch { }
        GC.SuppressFinalize(this);
    }

    private static ComposeSecret Entry(string item, string file) =>
        new($"vw-{file}", file, new SecretMarker(item, "password"));

    private static SecretLookup LookupExcept(SecretMarker marker, string absent) =>
        marker.Item == absent
            ? SecretLookup.Missing($"{absent} : introuvable")
            : SecretLookup.Found("valeur-" + marker.Item);

    // ───────────── La règle qui compte ─────────────

    /// <summary>
    /// Une clé absente n'écrit pas son fichier — <b>et les autres sont écrits quand même</b>.
    /// </summary>
    /// <remarks>
    /// C'est la moitié dangereuse du rendu partiel. Vaultwarden rogne ce qu'il lit via
    /// <c>_FILE</c>, mais <c>containerboot</c> lit <c>TS_AUTHKEY</c> par <c>file:</c> sans rien
    /// roger : un fichier vide le ferait partir avec une chaîne vide et échouer bien plus loin.
    /// Ne pas écrire est bruyant ; écrire du vide est silencieux.
    /// </remarks>
    [Fact]
    public void UneCleAbsente_NEcritPasSonFichier_MaisEcritLesAutres()
    {
        var result = SecretBundle.Resolve(
            [Entry("a", "ts-authkey"), Entry("absent", "smtp-password"), Entry("c", "admin-token")],
            m => LookupExcept(m, "absent"));

        Assert.Equal(["ts-authkey", "admin-token"], result.Files.Select(f => f.Name));
        Assert.DoesNotContain(result.Files, f => f.Name == "smtp-password");
        Assert.False(result.Complete);
        Assert.Single(result.Missing);
    }

    [Fact]
    public void ToutResolu_EstComplet_EtNeProposeRienALaSuppression()
    {
        var result = SecretBundle.Resolve(
            [Entry("a", "ts-authkey"), Entry("b", "smtp-password")],
            m => LookupExcept(m, "aucun"));

        Assert.True(result.Complete);
        Assert.Empty(result.Stale);
        Assert.Equal(2, result.Files.Count);
    }

    /// <summary>Les items distincts lus, pas le nombre de fichiers : cinq secrets, un seul item.</summary>
    [Fact]
    public void CompteLesItemsDistincts_PasLesFichiers()
    {
        var result = SecretBundle.Resolve(
            [Entry("infra", "a"), Entry("infra", "b"), Entry("infra", "c")],
            m => LookupExcept(m, "aucun"));

        Assert.Equal(3, result.Files.Count);
        Assert.Equal(1, result.ItemCount);
    }

    /// <summary>Une valeur vide n'est pas une valeur : le champ existe mais ne porte rien.</summary>
    [Fact]
    public void UneValeurVide_CompteCommeAbsente()
    {
        var result = SecretBundle.Resolve([Entry("a", "ts-authkey")], _ => SecretLookup.Found(""));

        Assert.Empty(result.Files);
        Assert.Single(result.Missing);
        Assert.Equal(["ts-authkey"], result.Stale);
    }

    // ───────────── Les périmés : signalés, jamais supprimés seuls ─────────────

    [Fact]
    public void UneCleAbsente_DesigneSonFichierCommePerime()
    {
        var result = SecretBundle.Resolve(
            [Entry("a", "ts-authkey"), Entry("absent", "smtp-password")],
            m => LookupExcept(m, "absent"));

        Assert.Equal(["smtp-password"], result.Stale);
    }

    /// <summary>
    /// Ne sont proposés que les fichiers qui <b>existent</b> : annoncer la suppression d'un fichier
    /// absent ferait douter de ce que la fenêtre sait du disque.
    /// </summary>
    [Fact]
    public void SeulsLesFichiersPresents_SontProposes()
    {
        var target = Path.Combine(_dir, SecretFileWriter.FolderName);
        Directory.CreateDirectory(target);
        File.WriteAllText(Path.Combine(target, "smtp-password"), "ancien");

        var existing = SecretFileWriter.Existing(_dir, ["smtp-password", "jamais-ecrit"]);

        Assert.Equal(["smtp-password"], existing);
    }

    /// <summary>
    /// <b>Jamais un balayage du dossier.</b> Un fichier que DockPad n'a pas écrit — un
    /// <c>.gitignore</c>, un secret posé à la main — ne doit pas disparaître parce qu'on faisait
    /// le ménage à côté.
    /// </summary>
    [Fact]
    public void LaSuppression_NeToucheQueLesNomsDemandes()
    {
        var target = Path.Combine(_dir, SecretFileWriter.FolderName);
        Directory.CreateDirectory(target);
        File.WriteAllText(Path.Combine(target, "smtp-password"), "perime");
        File.WriteAllText(Path.Combine(target, "pose-a-la-main"), "etranger");

        var deleted = SecretFileWriter.Delete(_dir, ["smtp-password"]);

        Assert.Equal(["smtp-password"], deleted);
        Assert.False(File.Exists(Path.Combine(target, "smtp-password")));
        Assert.True(File.Exists(Path.Combine(target, "pose-a-la-main")));
    }

    /// <summary>
    /// Un nom qui sortirait du dossier n'est ni écrit ni supprimé — la même garde des deux côtés.
    /// </summary>
    [Fact]
    public void LaSuppression_RefuseUnNomQuiSortiraitDuDossier()
    {
        var voisin = Path.Combine(_dir, "voisin.txt");
        File.WriteAllText(voisin, "a garder");
        Directory.CreateDirectory(Path.Combine(_dir, SecretFileWriter.FolderName));

        var deleted = SecretFileWriter.Delete(_dir, ["..\\voisin.txt", "../voisin.txt", ".."]);

        Assert.Empty(deleted);
        Assert.True(File.Exists(voisin));
    }

    /// <summary>Supprimer ce qui n'est pas là ne lève pas et ne prétend rien.</summary>
    [Fact]
    public void LaSuppression_DUnFichierAbsent_NeRendRien()
    {
        Directory.CreateDirectory(Path.Combine(_dir, SecretFileWriter.FolderName));

        Assert.Empty(SecretFileWriter.Delete(_dir, ["jamais-ecrit"]));
    }

    // ───────────── `template:` — la garde de chemin ─────────────

    [Fact]
    public void UnCheminDeModele_RelatifEtSousLeDossier_EstAccepte()
    {
        var full = SecretTemplatePath.Resolve(_dir, "templates/ntfy-config/server.yml");

        Assert.NotNull(full);
        Assert.StartsWith(Path.GetFullPath(_dir), full);
    }

    /// <summary>
    /// La garde qui compte : <c>template:</c> est la seule annotation qui désigne <b>quoi lire</b>,
    /// et elle vient d'un fichier. Sans elle, un compose ferait lire n'importe quoi sur la machine
    /// et l'écrirait, rendu, dans <c>secrets/</c>.
    /// </summary>
    /// <remarks>
    /// On compare les chemins <b>résolus</b>, jamais la chaîne : chercher <c>..</c> dedans se
    /// contourne par des séparateurs mélangés ou un chemin court 8.3.
    /// </remarks>
    [Theory]
    [InlineData("../voisin/secret.yml")]
    [InlineData("..\\voisin\\secret.yml")]
    [InlineData("templates/../../dehors.yml")]
    [InlineData("C:\\Users\\moi\\.ssh\\id_rsa")]
    [InlineData("/etc/passwd")]
    [InlineData("\\\\serveur\\partage\\secret.yml")]
    [InlineData("")]
    public void UnCheminDeModele_QuiSortDuDossier_EstRefuse(string relative)
        => Assert.Null(SecretTemplatePath.Resolve(_dir, relative));

    // ───────────── `template:` — le rendu ─────────────

    private static ComposeSecret Templated(string file, string template) =>
        new($"vw-{file}", file, null, template);

    [Fact]
    public void UnModele_EstRenduAvecLesValeursDuCoffre()
    {
        var result = SecretBundle.Resolve(
            [Templated("server.yml", "templates/server.yml")],
            m => SecretLookup.Found("hash-" + m.Field),
            new Dictionary<string, string>
            {
                ["templates/server.yml"] = "auth-users:\n  - \"mobile:{{ bw:ntfy:hash-mobile }}:user\"\n",
            });

        Assert.True(result.Complete);
        Assert.Equal("auth-users:\n  - \"mobile:hash-hash-mobile:user\"\n", result.Files.Single().Value);
        Assert.Equal("server.yml", result.Files.Single().Name);
    }

    /// <summary>
    /// <b>Tout ou rien, par fichier</b> — asymétrie assumée avec le presse-papier.
    /// </summary>
    /// <remarks>
    /// Là, un marqueur non résolu reste littéral parce que l'utilisateur le <i>voit</i> dans ce
    /// qu'il colle. Ici le fichier part sur le NAS sans que personne ne le relise : un
    /// <c>{{ bw:… }}</c> déposé tel quel deviendrait un hachage bcrypt invalide, et le service
    /// refuserait le compte sans dire pourquoi.
    /// </remarks>
    [Fact]
    public void UnMarqueurManquantDansUnModele_NEcritPasCeFichier_MaisEcritLesAutres()
    {
        var result = SecretBundle.Resolve(
            [Entry("a", "ts-authkey"), Templated("server.yml", "templates/server.yml")],
            m => m.Field == "absent" ? SecretLookup.Missing("absent du coffre") : SecretLookup.Found("v"),
            new Dictionary<string, string>
            {
                ["templates/server.yml"] = "ok: {{ bw:ntfy:present }}\nko: {{ bw:ntfy:absent }}\n",
            });

        Assert.Equal(["ts-authkey"], result.Files.Select(f => f.Name));
        Assert.DoesNotContain(result.Files, f => f.Name == "server.yml");
        Assert.Contains("server.yml", result.Stale);
        Assert.False(result.Complete);
    }

    /// <summary>
    /// Un modèle sans marqueur est valide — c'est un fichier de structure recopié tel quel.
    /// Contrairement au presse-papier, où l'absence de marqueur signale qu'on a visé le mauvais
    /// fichier, ici c'est le compose qui a désigné ce modèle : l'intention est explicite.
    /// </summary>
    [Fact]
    public void UnModeleSansMarqueur_EstRecopieTelQuel()
    {
        var result = SecretBundle.Resolve(
            [Templated("server.yml", "t.yml")],
            _ => SecretLookup.Missing("jamais appele"),
            new Dictionary<string, string> { ["t.yml"] = "listen: 127.0.0.1:8080\n" });

        Assert.True(result.Complete);
        Assert.Equal("listen: 127.0.0.1:8080\n", result.Files.Single().Value);
    }

    /// <summary>
    /// Le modèle vient d'un dépôt git, qui peut l'avoir extrait en CRLF sous Windows ; la
    /// destination est un conteneur Linux.
    /// </summary>
    [Fact]
    public void LesFinsDeLigneDUnModele_SontNormaliseesEnLF()
    {
        var result = SecretBundle.Resolve(
            [Templated("server.yml", "t.yml")],
            _ => SecretLookup.Found("x"),
            new Dictionary<string, string> { ["t.yml"] = "a: 1\r\nb: {{ bw:i:f }}\r\n" });

        Assert.Equal("a: 1\nb: x\n", result.Files.Single().Value);
    }

    /// <summary>
    /// Une <b>valeur</b> du coffre n'est jamais normalisée : c'est un secret, on l'écrit telle
    /// qu'elle est. Seuls les modèles le sont.
    /// </summary>
    [Fact]
    public void UneValeurDuCoffre_NEstJamaisNormalisee()
    {
        var result = SecretBundle.Resolve(
            [Entry("a", "ts-authkey")], _ => SecretLookup.Found("tskey\r\nsuite"));

        Assert.Equal("tskey\r\nsuite", result.Files.Single().Value);
    }

    /// <summary>Un modèle que l'appelant n'a pas lu ne produit rien, et le dit.</summary>
    [Fact]
    public void UnModeleNonFourni_NEcritRien()
    {
        var result = SecretBundle.Resolve(
            [Templated("server.yml", "t.yml")], _ => SecretLookup.Found("x"));

        Assert.Empty(result.Files);
        Assert.Single(result.Missing);
    }

    // ───────────── `template:` — l'annotation ─────────────

    private static string Compose(string annotation) =>
        "secrets:\n  ntfy-config:\n    file: /share/x/secrets/server.yml\n    x-bw:\n" + annotation;

    [Fact]
    public void UneAnnotationTemplate_EstLue()
    {
        var scan = ComposeSecrets.Extract(Compose("      template: templates/server.yml\n"));

        var entry = Assert.Single(scan.Entries);
        Assert.Equal("templates/server.yml", entry.Template);
        Assert.Null(entry.Marker);
        Assert.Equal("server.yml", entry.FileName);
    }

    /// <summary>Deux sources pour un même fichier : refus, jamais un choix implicite.</summary>
    [Fact]
    public void UneAnnotationQuiPorteLesDeuxSources_EstUnRefus()
    {
        var scan = ComposeSecrets.Extract(
            Compose("      item: ntfy-infra\n      field: f\n      template: templates/server.yml\n"));

        Assert.Empty(scan.Entries);
        Assert.Single(scan.Failures);
    }

    [Fact]
    public void UneAnnotationSansAucuneSource_EstUnRefus()
    {
        var scan = ComposeSecrets.Extract(Compose("      item: ntfy-infra\n"));

        Assert.Empty(scan.Entries);
        Assert.Single(scan.Failures);
    }
}
