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
}
