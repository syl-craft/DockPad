using DockPad.Models;
using DockPad.Services;

namespace DockPad.Tests;

/// <summary>
/// Filtrage de la barre de recherche.
/// </summary>
/// <remarks>
/// La règle — sur quoi on cherche, dans quel ordre on rend, ce qu'on fait d'une requête vide —
/// était noyée dans un gestionnaire <c>TextChanged</c> avec le chargement des icônes et l'ouverture
/// du popup. Séparée, elle se teste ; mêlée, elle ne se vérifiait qu'en tapant dans l'application.
/// </remarks>
public class ShortcutSearchTests
{
    private static List<ShortcutEntry> Entries(params string[] names) =>
        names.Select((n, i) => new ShortcutEntry { Name = n, Page = i / 24, Row = i % 4, Col = i % 6 }).ToList();

    [Fact]
    public void CherchePartoutDansLeNom_PasSeulementAuDebut()
    {
        // « code » doit trouver « VS Code » : chercher un préfixe rendrait la barre inutile pour
        // les noms composés, qui sont la majorité.
        var found = ShortcutSearch.Filter(Entries("VS Code", "Terminal"), "code");

        Assert.Equal(["VS Code"], found.Select(e => e.Name));
    }

    [Fact]
    public void IgnoreLaCasse()
    {
        var found = ShortcutSearch.Filter(Entries("GitHub"), "GITHUB");

        Assert.Single(found);
    }

    [Fact]
    public void RendParOrdreAlphabetique_PasParPosition()
    {
        // Les résultats viennent de toutes les pages : l'ordre des cases ne veut rien dire pour
        // celui qui lit la liste.
        var found = ShortcutSearch.Filter(Entries("Zeta", "Alpha", "Banane"), "a");

        Assert.Equal(["Alpha", "Banane", "Zeta"], found.Select(e => e.Name));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void RequeteVide_NeRendRien(string query)
    {
        // Rien, et non « tout » : la barre vide ne doit pas ouvrir un popup de vingt-quatre lignes.
        Assert.Empty(ShortcutSearch.Filter(Entries("Alpha", "Beta"), query));
    }

    [Fact]
    public void EspacesAutourDeLaRequete_SontIgnores()
    {
        Assert.Single(ShortcutSearch.Filter(Entries("Alpha"), "  alph  "));
    }

    [Fact]
    public void TuileSansNom_NEstJamaisTrouvee()
    {
        // Une case vide porte un nom vide : sans ce filtre, elle remonterait sur chaque recherche,
        // puisque toute chaîne contient la chaîne vide.
        var entries = Entries("Alpha");
        entries.Add(new ShortcutEntry { Name = "" });

        var found = ShortcutSearch.Filter(entries, "a");

        Assert.Single(found);
    }
}
