using System.IO;
using DockPad.Models;
using DockPad.Services;

namespace DockPad.Tests;

/// <summary>
/// Ce que décide le toggle étoile du popup de choix du navigateur.
/// </summary>
/// <remarks>
/// Tout ce qui suit est <b>pur</b> : trouver un favori existant, choisir sa case, le nommer. La
/// fenêtre ne fait qu'appeler et afficher — c'est ce qui rend le geste vérifiable sans WPF, et sans
/// ouvrir de navigateur.
/// </remarks>
public class FavoriteToggleTests
{
    private static ShortcutEntry Url(string url, int page = 0, int row = 0, int col = 0) =>
        new() { Page = page, Row = row, Col = col, Name = "x",
                Type = ShortcutType.OpenUrl, Command = url };

    private static List<ShortcutEntry> FullPage(int page)
    {
        var list = new List<ShortcutEntry>();
        for (int r = 0; r < ShortcutActionService.GridRows; r++)
            for (int c = 0; c < ShortcutActionService.GridCols; c++)
                list.Add(Url($"https://p{page}-{r}-{c}.test", page, r, c));
        return list;
    }

    // ── Trouver ──────────────────────────────────────────────────────────────

    [Fact]
    public void Find_UrlExacte_TrouveLeFavori()
    {
        var all = new List<ShortcutEntry> { Url("https://github.com/user/repo/issues/42") };

        Assert.NotNull(FavoriteToggle.Find(all, "https://github.com/user/repo/issues/42"));
    }

    [Fact]
    public void Find_AutreUrlDuMemeSite_NeTrouveRien()
    {
        // Conséquence assumée de « URL complète » : le toggle parle de CETTE page, pas du site.
        var all = new List<ShortcutEntry> { Url("https://github.com/user/repo/issues/42") };

        Assert.Null(FavoriteToggle.Find(all, "https://github.com/user/repo"));
    }

    [Fact]
    public void Find_MemeCommandeMaisPasUneTuileWeb_NeTrouveRien()
    {
        // Une tuile qui lance une commande n'est pas un favori de site, même si la chaîne coïncide.
        var all = new List<ShortcutEntry>
        {
            new() { Name = "x", Type = ShortcutType.RunCommand, Command = "https://github.com" },
        };

        Assert.Null(FavoriteToggle.Find(all, "https://github.com"));
    }

    // ── Placer ────────────────────────────────────────────────────────────────
    //
    // Placement choisit la PAGE ; c'est AddCore qui choisit la case dans cette page. Une seule
    // règle par endroit : dupliquer le balayage des cases ici, c'était deux implémentations à
    // garder d'accord, et une page créée pour rien quand la seconde refusait ce que la première
    // avait promis.

    [Fact]
    public void Placement_GrilleVide_PremierePage()
    {
        var p = FavoriteToggle.Placement([], []);

        Assert.Equal(0, p.Page);
        Assert.False(p.NeedsNewPage);
    }

    [Fact]
    public void Placement_PageEntamee_YReste()
    {
        var all = new List<ShortcutEntry> { Url("https://a.test", 0, 0, 0), Url("https://b.test", 0, 0, 1) };

        var p = FavoriteToggle.Placement(all, []);

        Assert.Equal(0, p.Page);
        Assert.False(p.NeedsNewPage);
    }

    [Fact]
    public void Placement_TrouLaisseParUneSuppression_LaPageCompteCommeLibre()
    {
        var all = FullPage(0);
        all.RemoveAll(s => s is { Row: 2, Col: 3 });

        var p = FavoriteToggle.Placement(all, []);

        Assert.Equal(0, p.Page);
        Assert.False(p.NeedsNewPage);
    }

    [Fact]
    public void Placement_PremierePagePleine_PasseALaSuivante()
    {
        // C'est ce qui distingue cette règle de celle d'AddCore, qui ne regarde que la page 0
        // et refuse. Le popup ne peut pas refuser : personne n'est là pour lire le message.
        var all = FullPage(0);
        all.Add(Url("https://x.test", 1, 0, 0));

        var p = FavoriteToggle.Placement(all, []);

        Assert.Equal(1, p.Page);
        Assert.False(p.NeedsNewPage);
    }

    [Fact]
    public void Placement_PageDeclareeVide_YAtterrit()
    {
        // Une page peut exister sans aucune tuile : elle est déclarée dans le fichier des pages.
        var all = FullPage(0);
        var configs = new List<PageConfig> { new() { Index = 1 } };

        var p = FavoriteToggle.Placement(all, configs);

        Assert.Equal(1, p.Page);
        Assert.False(p.NeedsNewPage);
    }

    [Fact]
    public void Placement_ToutesLesPagesPleines_EnDemandeUneNouvelle()
    {
        var all = FullPage(0);
        all.AddRange(FullPage(1));

        var p = FavoriteToggle.Placement(all, []);

        Assert.Equal(2, p.Page);
        Assert.True(p.NeedsNewPage);
    }

    // ── Nommer ───────────────────────────────────────────────────────────────

    [Fact]
    public void NameFor_EstLeDomaine_PasLUrlEntiere()
    {
        // Le nom s'affiche sur 94 px : une URL complète y serait illisible.
        Assert.Equal("github.com", FavoriteToggle.NameFor("https://github.com/user/repo/issues/42"));
    }

    [Fact]
    public void NameFor_GardeUnPortNonStandard()
    {
        // Deux services sur la même machine ne sont pas le même favori.
        Assert.Equal("localhost:44351", FavoriteToggle.NameFor("https://localhost:44351/admin"));
    }

    [Fact]
    public void NameFor_UrlNonAnalysable_RetombeSurLUrl()
    {
        // Une tuile sans nom est invisible dans la grille : mieux vaut un nom laid qu'aucun.
        Assert.Equal("pas une url", FavoriteToggle.NameFor("pas une url"));
    }

    // ── Poser et retirer ─────────────────────────────────────────────────────
    //
    // Seuls les chemins qui NE partent PAS sur le réseau sont testés ici. Poser un favori déclenche
    // le téléchargement de l'icône du site — c'est voulu, et c'est aussi ce qui rend ce chemin
    // intestable sans sortir de la machine : un test qui appelle un service tiers casse un jour
    // sans raison. Lacune nommée plutôt que test de façade.

    [Fact]
    public async Task SetAsync_Retirer_SupprimeLaTuileEtRienDAutre()
    {
        using var dir = new TempProfile();
        ShortcutService.Save(
        [
            Url("https://a.test", 0, 0, 0),
            Url("https://b.test", 0, 0, 1),
        ], dir.Files.EntriesPath);

        bool state = await FavoriteToggle.SetAsync("https://a.test", favorite: false, dir.Files);

        Assert.False(state);
        var rest = ShortcutService.Load(dir.Files.EntriesPath);
        Assert.Equal("https://b.test", Assert.Single(rest).Command);
    }

    [Fact]
    public async Task SetAsync_RetirerCeQuiNEstPasFavori_NeCasseRien()
    {
        // Le toggle peut être décoché sur une URL absente du fichier : deux popups ouverts sur la
        // même page, l'un retire, l'autre retire encore.
        using var dir = new TempProfile();
        ShortcutService.Save([Url("https://b.test", 0, 0, 0)], dir.Files.EntriesPath);

        bool state = await FavoriteToggle.SetAsync("https://a.test", favorite: false, dir.Files);

        Assert.False(state);
        Assert.Single(ShortcutService.Load(dir.Files.EntriesPath));
    }

    [Fact]
    public async Task SetAsync_DejaFavori_NeCreePasDeDoublon()
    {
        // Chemin sans réseau : l'entrée existe, on sort avant tout téléchargement.
        using var dir = new TempProfile();
        ShortcutService.Save([Url("https://a.test", 0, 0, 0)], dir.Files.EntriesPath);

        bool state = await FavoriteToggle.SetAsync("https://a.test", favorite: true, dir.Files);

        Assert.True(state);
        Assert.Single(ShortcutService.Load(dir.Files.EntriesPath));
    }

    private sealed class TempProfile : IDisposable
    {
        private readonly string _dir = Path.Combine(Path.GetTempPath(), $"fav_{Guid.NewGuid():N}");

        public TempProfile() => Directory.CreateDirectory(_dir);

        public TileFiles Files => new(Path.Combine(_dir, "favorites.json"),
                                      Path.Combine(_dir, "favorite-pages.json"));

        public void Dispose()
        {
            if (Directory.Exists(_dir)) Directory.Delete(_dir, recursive: true);
        }
    }
}
