using System.IO;
using DockPad.Models;
using DockPad.Services;

namespace DockPad.Tests;

/// <summary>
/// Le seul endroit qui sait quel fichier porte quelles tuiles.
/// </summary>
/// <remarks>
/// La panne que ces tests existent pour empêcher est la plus coûteuse de la feature : des favoris
/// écrits par-dessus les raccourcis. Elle ne se voit qu'après coup, et rien ne la rattrape.
/// </remarks>
public class TileStoreTests
{
    [Fact]
    public void Shortcuts_GardeLesFichiersHistoriques()
    {
        // Le nom de fichier ne peut pas changer : c'est la config de tous les utilisateurs
        // existants, et personne ne migre un fichier renommé en silence.
        Assert.Equal("shortcuts.json", Path.GetFileName(TileStore.EntriesPath(TileTarget.Shortcuts)));
        Assert.Equal("pages.json", Path.GetFileName(TileStore.PagesPath(TileTarget.Shortcuts)));
    }

    [Fact]
    public void Favorites_APropresFichiers()
    {
        Assert.Equal("favorites.json", Path.GetFileName(TileStore.EntriesPath(TileTarget.Favorites)));
        Assert.Equal("favorite-pages.json", Path.GetFileName(TileStore.PagesPath(TileTarget.Favorites)));
    }

    [Fact]
    public void LesDeuxCibles_NePartagentAucunFichier()
    {
        // Le test qui mord : un copier-coller qui laisserait la même constante des deux côtés
        // ferait écrire les favoris dans shortcuts.json, et il n'y a pas de retour en arrière.
        var paths = new[]
        {
            TileStore.EntriesPath(TileTarget.Shortcuts),
            TileStore.PagesPath(TileTarget.Shortcuts),
            TileStore.EntriesPath(TileTarget.Favorites),
            TileStore.PagesPath(TileTarget.Favorites),
        };

        Assert.Equal(paths.Length, paths.Distinct(StringComparer.OrdinalIgnoreCase).Count());
    }

    [Fact]
    public void LesQuatreFichiers_ViventDansLeProfil()
    {
        // Donc ils suivent DOCKPAD_PROFILE_DIR : un profil portable qui n'emporterait pas les
        // favoris serait un profil portable à moitié.
        foreach (var target in new[] { TileTarget.Shortcuts, TileTarget.Favorites })
        {
            Assert.Equal(AppPaths.ProfileRoot, Path.GetDirectoryName(TileStore.EntriesPath(target)));
            Assert.Equal(AppPaths.ProfileRoot, Path.GetDirectoryName(TileStore.PagesPath(target)));
        }
    }

    [Fact]
    public void Parse_ReconnaitLesDeuxCibles_SansCasse()
    {
        // C'est ce que le serveur MCP reçoit : une chaîne écrite par un modèle.
        Assert.Equal(TileTarget.Shortcuts, TileStore.Parse("shortcuts"));
        Assert.Equal(TileTarget.Favorites, TileStore.Parse("favorites"));
        Assert.Equal(TileTarget.Favorites, TileStore.Parse("Favorites"));
    }

    [Fact]
    public void Parse_AbsentOuVide_RetombeSurLesRaccourcis()
    {
        // Le défaut ne peut pas changer de sens : les appels MCP existants ne connaissent pas
        // le paramètre.
        Assert.Equal(TileTarget.Shortcuts, TileStore.Parse(null));
        Assert.Equal(TileTarget.Shortcuts, TileStore.Parse(""));
        Assert.Equal(TileTarget.Shortcuts, TileStore.Parse("   "));
    }

    [Fact]
    public void Parse_ValeurInconnue_Leve()
    {
        // Un repli silencieux sur les raccourcis écrirait dans la mauvaise grille sans le dire :
        // c'est le pire des deux comportements.
        var ex = Assert.Throws<ArgumentException>(() => TileStore.Parse("bookmarks"));
        Assert.Contains("bookmarks", ex.Message);
    }
}
