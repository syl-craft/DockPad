using System.Globalization;
using DockPad.Secrets;
using DockPad.Services.Localization;

namespace DockPad.Tests.Secrets;

/// <summary>
/// L'ordre de résolution d'un champ, et les trois cas de résolution d'un item.
/// </summary>
public class SecretFieldResolverTests
{
    public SecretFieldResolverTests() => Loc.SetCulture(CultureInfo.GetCultureInfo("fr"));

    private static BwItem Item(string name, params (string Name, string Value)[] fields) => new()
    {
        Name = name,
        Fields = fields.Select(f => new BwField { Name = f.Name, Value = f.Value }).ToList(),
    };

    // ───────────── L'ordre des champs ─────────────

    [Fact]
    public void UnChampPersonnaliseEstCherchePremier()
    {
        var item = Item("ntfy", ("token", "tk_42"));

        Assert.Equal("tk_42", SecretFieldResolver.Resolve(item, "token"));
    }

    [Fact]
    public void UnChampPersonnaliseGagneSurLeChampStandardDuMemeNom()
    {
        // C'est l'ordre du script d'origine : ce qu'on a nommé soi-même prime sur le champ que
        // Bitwarden fournit d'office.
        var item = Item("ntfy", ("password", "celui-du-champ-perso"));
        item.Login = new BwLogin { Password = "celui-de-la-fiche" };

        Assert.Equal("celui-du-champ-perso", SecretFieldResolver.Resolve(item, "password"));
    }

    [Fact]
    public void ResoutLesQuatreChampsStandards()
    {
        var item = new BwItem
        {
            Name = "ntfy",
            Notes = "note",
            Login = new BwLogin { Username = "sylvain", Password = "s3cr3t", Totp = "otpauth://x" },
        };

        Assert.Equal("s3cr3t", SecretFieldResolver.Resolve(item, "password"));
        Assert.Equal("sylvain", SecretFieldResolver.Resolve(item, "username"));
        Assert.Equal("note", SecretFieldResolver.Resolve(item, "notes"));
        Assert.Equal("otpauth://x", SecretFieldResolver.Resolve(item, "totp"));
    }

    [Fact]
    public void UnChampInconnu_NeDonneRien()
    {
        Assert.Null(SecretFieldResolver.Resolve(Item("ntfy"), "inexistant"));
    }

    [Fact]
    public void UnChampPersonnaliseVide_NeRetombePasSurLeChampStandard()
    {
        // Le champ nommé existe et il est vide : le dire franchement vaut mieux que d'aller
        // chercher ailleurs une valeur que personne n'a demandée.
        var item = Item("ntfy", ("password", ""));
        item.Login = new BwLogin { Password = "celui-de-la-fiche" };

        Assert.True(string.IsNullOrEmpty(SecretFieldResolver.Resolve(item, "password")));
    }

    [Fact]
    public void UneFicheSansLogin_NeLevePas()
    {
        Assert.Null(SecretFieldResolver.Resolve(new BwItem { Name = "ntfy" }, "password"));
    }

    // ───────────── Les trois cas de résolution d'un item ─────────────

    [Fact]
    public void UnItemTrouve_RendSaValeur()
    {
        var vault = new SecretVault([Item("ntfy", ("token", "tk_42"))], organisation: "");

        Assert.Equal("tk_42", vault.Lookup(new SecretMarker("ntfy", "token")).Value);
    }

    [Fact]
    public void UnItemAbsent_AvecOrganisation_NommeLOrganisation()
    {
        var vault = new SecretVault([], organisation: "Infra maison");

        var found = vault.Lookup(new SecretMarker("ntfy", "token"));

        Assert.Null(found.Value);
        Assert.Equal(Loc.F("Inject_Error_ItemMissingOrg", "ntfy", "Infra maison"), found.Failure);
    }

    [Fact]
    public void UnItemAbsent_SansOrganisation_ParleDuCoffre()
    {
        var vault = new SecretVault([], organisation: "");

        Assert.Equal(Loc.F("Inject_Error_ItemMissingVault", "ntfy"),
            vault.Lookup(new SecretMarker("ntfy", "token")).Failure);
    }

    [Fact]
    public void DeuxItemsDuMemeNom_SontUneAmbiguiteNommee()
    {
        // C'est ce que l'organisation sert à éviter. On le détecte soi-même plutôt que de laisser
        // la CLI répondre « More than one result », qui ne dit pas quoi renommer.
        var vault = new SecretVault([Item("ntfy", ("token", "a")), Item("ntfy", ("token", "b"))], "");

        Assert.Equal(Loc.F("Inject_Error_ItemAmbiguous", "ntfy"),
            vault.Lookup(new SecretMarker("ntfy", "token")).Failure);
    }

    [Fact]
    public void DeuxItemsQuiNeDifferentQueParLaCasse_SontAussiUneAmbiguite()
    {
        // La correspondance ignore la casse, donc ces deux-là se disputent le même marqueur.
        var vault = new SecretVault([Item("ntfy", ("token", "a")), Item("NTFY", ("token", "b"))], "");

        Assert.Equal(Loc.F("Inject_Error_ItemAmbiguous", "ntfy"),
            vault.Lookup(new SecretMarker("ntfy", "token")).Failure);
    }

    [Fact]
    public void LaCorrespondanceDuNomEstExacte_PasUneSousChaine()
    {
        // « ntfy-old » ne doit pas répondre pour « ntfy » : on livrerait le secret d'un autre
        // service sans que rien ne le signale.
        var vault = new SecretVault([Item("ntfy-old", ("token", "vieux"))], "");

        Assert.Null(vault.Lookup(new SecretMarker("ntfy", "token")).Value);
    }

    [Fact]
    public void UnItemTrouveMaisUnChampAbsent_NommeLeChamp()
    {
        var vault = new SecretVault([Item("ntfy", ("token", "tk"))], "");

        Assert.Equal(Loc.F("Inject_Error_EmptyField", "ntfy", "absent"),
            vault.Lookup(new SecretMarker("ntfy", "absent")).Failure);
    }
}
