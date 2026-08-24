using System.IO;
using DockPad.Models;
using DockPad.Services;

namespace DockPad.Tests;

/// <summary>
/// Ce qu'on fabrique à partir d'un fichier glissé depuis l'Explorateur.
/// </summary>
/// <remarks>
/// La lecture d'un raccourci Internet est un petit format de fichier à part entière — sections,
/// clés, encodage, lignes inattendues. Elle vivait dans le code-behind, sans test : le seul moyen de
/// vérifier un cas limite était de fabriquer un <c>.url</c> et de le glisser sur la fenêtre.
/// </remarks>
public class DroppedFileTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), $"drop_{Guid.NewGuid():N}");

    public DroppedFileTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        if (Directory.Exists(_dir)) Directory.Delete(_dir, recursive: true);
    }

    private string WriteUrlFile(string name, params string[] lines)
    {
        var path = Path.Combine(_dir, name);
        File.WriteAllLines(path, lines);
        return path;
    }

    [Fact]
    public void FichierUrl_RendLUrlEtSonTitre()
    {
        var path = WriteUrlFile("lien.url", "[InternetShortcut]", "URL=https://claude.ai", "Title=Claude");

        var dropped = DroppedShortcut.FromUrlFile(path);

        Assert.Equal("https://claude.ai", dropped!.Url);
        Assert.Equal("Claude", dropped.Name);
    }

    [Fact]
    public void FichierUrl_SansTitre_PrendLeNomDuFichier()
    {
        // Les .url exportés par Chrome et Edge ne portent pas de Title : sans ce repli, la tuile
        // s'appellerait « ».
        var path = WriteUrlFile("Mon site.url", "[InternetShortcut]", "URL=https://exemple.fr");

        var dropped = DroppedShortcut.FromUrlFile(path);

        Assert.Equal("Mon site", dropped!.Name);
    }

    [Fact]
    public void FichierUrl_CleEnCasseQuelconque_EstReconnue()
    {
        var path = WriteUrlFile("lien.url", "url=https://exemple.fr", "TITLE=Exemple");

        var dropped = DroppedShortcut.FromUrlFile(path);

        Assert.Equal("https://exemple.fr", dropped!.Url);
        Assert.Equal("Exemple", dropped.Name);
    }

    [Fact]
    public void FichierUrl_SansUrl_NeDonneRien()
    {
        // Un .url sans URL ne peut pas faire une tuile : mieux vaut ne rien créer qu'une tuile morte.
        var path = WriteUrlFile("vide.url", "[InternetShortcut]", "IconIndex=0");

        Assert.Null(DroppedShortcut.FromUrlFile(path));
    }

    [Fact]
    public void FichierAbsent_NeDonneRienEtNeLevePas()
    {
        Assert.Null(DroppedShortcut.FromUrlFile(Path.Combine(_dir, "absent.url")));
    }

    [Theory]
    [InlineData(@"C:\dev\projets", "projets")]
    [InlineData(@"C:\dev\projets\", "projets")]
    [InlineData(@"C:\", "C:")]
    public void Dossier_LeNomDeLaTuileEstCeluiDuDossier(string path, string expected)
    {
        // Le cas de la racine est le piège : Path.GetFileName rend une chaîne vide sur « C:\ ».
        Assert.Equal(expected, DroppedShortcut.FolderName(path));
    }

    [Fact]
    public void EstUnDepotAcceptable_DossierOuFichierUrlSeulement()
    {
        Assert.True(DroppedShortcut.IsAcceptable(_dir));                       // un dossier
        Assert.True(DroppedShortcut.IsAcceptable(WriteUrlFile("a.url", "x"))); // un .url
        Assert.False(DroppedShortcut.IsAcceptable(Path.Combine(_dir, "photo.png")));
        Assert.False(DroppedShortcut.IsAcceptable(""));
    }
}
