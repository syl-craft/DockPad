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

    // ── Placer ───────────────────────────────────────────────────────────────

    [Fact]
    public void Placement_GrilleVide_PremiereCaseDeLaPremierePage()
    {
        var p = FavoriteToggle.Placement([], []);

        Assert.Equal((0, 0, 0), (p.Page, p.Row, p.Col));
        Assert.False(p.NeedsNewPage);
    }

    [Fact]
    public void Placement_SuitLOrdreDeLecture()
    {
        var all = new List<ShortcutEntry> { Url("https://a.test", 0, 0, 0), Url("https://b.test", 0, 0, 1) };

        var p = FavoriteToggle.Placement(all, []);

        Assert.Equal((0, 0, 2), (p.Page, p.Row, p.Col));
    }

    [Fact]
    public void Placement_ReboucheUnTrouLaisseParUneSuppression()
    {
        // Le balayage cherche une case LIBRE, pas la suite du dernier ajout.
        var all = FullPage(0);
        all.RemoveAll(s => s is { Row: 2, Col: 3 });

        var p = FavoriteToggle.Placement(all, []);

        Assert.Equal((0, 2, 3), (p.Page, p.Row, p.Col));
    }

    [Fact]
    public void Placement_PremierePagePleine_PasseALaSuivante()
    {
        // C'est ce qui distingue cette règle de celle d'AddCore, qui ne regarde que la page 0
        // et refuse. Le popup ne peut pas refuser : personne n'est là pour lire le message.
        var all = FullPage(0);
        all.Add(Url("https://x.test", 1, 0, 0));

        var p = FavoriteToggle.Placement(all, []);

        Assert.Equal((1, 0, 1), (p.Page, p.Row, p.Col));
        Assert.False(p.NeedsNewPage);
    }

    [Fact]
    public void Placement_PageDeclareeVide_YAtterrit()
    {
        // Une page peut exister sans aucune tuile : elle est déclarée dans le fichier des pages.
        var all = FullPage(0);
        var configs = new List<PageConfig> { new() { Index = 1 } };

        var p = FavoriteToggle.Placement(all, configs);

        Assert.Equal((1, 0, 0), (p.Page, p.Row, p.Col));
        Assert.False(p.NeedsNewPage);
    }

    [Fact]
    public void Placement_ToutesLesPagesPleines_EnDemandeUneNouvelle()
    {
        var all = FullPage(0);
        all.AddRange(FullPage(1));

        var p = FavoriteToggle.Placement(all, []);

        Assert.Equal((2, 0, 0), (p.Page, p.Row, p.Col));
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
}
