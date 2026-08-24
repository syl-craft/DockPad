using System.IO;
using DockPad.Services;

namespace DockPad.Tests;

/// <summary>
/// Chargement d'une image d'icône.
/// </summary>
/// <remarks>
/// <para>
/// <c>new BitmapImage(new Uri(path))</c> laisse le <c>CacheOption</c> par défaut, <c>OnDemand</c>,
/// qui <b>garde le fichier ouvert</b> tant que l'image vit. Le store d'icônes réécrit et
/// resynchronise des fichiers — changement d'icône, ↻ Actualiser : le verrou finit par produire un
/// « fichier utilisé par un autre processus » sur le poste de l'utilisateur, jamais chez le
/// développeur qui ne garde pas l'application ouverte assez longtemps.
/// </para>
/// <para>
/// Ces tests n'ont besoin d'aucune instance <c>Application</c> : <c>BitmapImage</c> n'est pas un
/// élément d'interface.
/// </para>
/// </remarks>
public class IconLoadingTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), $"iconload_{Guid.NewGuid():N}");

    public IconLoadingTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        if (Directory.Exists(_dir)) Directory.Delete(_dir, recursive: true);
    }

    /// <summary>Un PNG minimal valide, 1×1 transparent.</summary>
    private string WritePng(string name = "icon.png")
    {
        var path = Path.Combine(_dir, name);
        File.WriteAllBytes(path, Convert.FromBase64String(
            "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mP8z8BQDwAEhQGAhKmMIQAAAABJRU5ErkJggg=="));
        return path;
    }

    [Fact]
    public void LoadImage_NeVerrouillePasLeFichier()
    {
        // Le test qui compte : après chargement, le fichier doit rester réécrivable et supprimable.
        var path = WritePng();

        var image = IconStoreService.LoadImage(path);

        Assert.NotNull(image);
        File.WriteAllBytes(path, File.ReadAllBytes(path));   // réécriture : échoue si verrouillé
        File.Delete(path);                                   // suppression : idem
        Assert.False(File.Exists(path));
    }

    [Fact]
    public void LoadImage_RendUneImageGelee()
    {
        // Une image gelée est moins chère et traversable entre threads. Sans gel, la charger depuis
        // un Task.Run lèverait à l'affichage.
        var image = IconStoreService.LoadImage(WritePng());

        Assert.NotNull(image);
        Assert.True(image!.IsFrozen);
    }

    [Fact]
    public void LoadImage_CheminVideOuInexistant_RendNull()
    {
        // Une icône manquante est un cas courant — entrée pointant un exe désinstallé — pas une
        // erreur : la tuile s'affiche sans image.
        Assert.Null(IconStoreService.LoadImage(""));
        Assert.Null(IconStoreService.LoadImage(null));
        Assert.Null(IconStoreService.LoadImage(Path.Combine(_dir, "absent.png")));
    }

    [Fact]
    public void LoadImage_FichierIllisible_RendNullSansLever()
    {
        // Un .png qui n'en est pas un : l'affichage doit survivre à une icône corrompue.
        var path = Path.Combine(_dir, "corrompu.png");
        File.WriteAllText(path, "ceci n'est pas une image");

        Assert.Null(IconStoreService.LoadImage(path));
    }
}
