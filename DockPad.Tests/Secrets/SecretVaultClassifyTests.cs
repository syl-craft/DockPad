using System.Globalization;
using DockPad.Secrets;
using DockPad.Services.Localization;

namespace DockPad.Tests.Secrets;

/// <summary>Les quatre cas qu'un marqueur peut rencontrer dans le coffre.</summary>
public class SecretVaultClassifyTests
{
    public SecretVaultClassifyTests() => Loc.SetCulture(CultureInfo.GetCultureInfo("fr"));

    private static BwItem Item(string id, string name, params (string Name, string Value)[] fields) => new()
    {
        Id = id,
        Name = name,
        Fields = fields.Select(f => new BwField { Name = f.Name, Value = f.Value }).ToList(),
    };

    private static SecretVault Vault(params BwItem[] items) => new(items, "");

    [Fact]
    public void UnChampRenseigne_EstTrouve()
    {
        var presence = Vault(Item("i1", "ntfy", ("token", "tk"))).Classify(new("ntfy", "token"));

        Assert.Equal(SecretPresenceKind.Found, presence.Kind);
        Assert.Equal("i1", presence.ItemId);
    }

    [Fact]
    public void UnItemAbsent_EstCreable()
    {
        var presence = Vault().Classify(new("ia-requester-infra", "db-password"));

        Assert.Equal(SecretPresenceKind.ItemMissing, presence.Kind);
        Assert.Null(presence.ItemId);
    }

    [Fact]
    public void UnChampAbsent_EstCompletable_SurLItemExistant()
    {
        var presence = Vault(Item("i1", "ntfy")).Classify(new("NTFY", "token"));

        Assert.Equal(SecretPresenceKind.FieldMissing, presence.Kind);
        Assert.Equal("i1", presence.ItemId);
    }

    [Fact]
    public void UnChampPersonnaliseVide_EstCompletable()
    {
        var presence = Vault(Item("i1", "ntfy", ("token", ""))).Classify(new("ntfy", "token"));

        Assert.Equal(SecretPresenceKind.FieldMissing, presence.Kind);
    }

    [Fact]
    public void DeuxItemsDuMemeNom_NeSontJamaisCreables()
    {
        var presence = Vault(Item("i1", "ntfy"), Item("i2", "ntfy")).Classify(new("ntfy", "token"));

        Assert.Equal(SecretPresenceKind.Ambiguous, presence.Kind);
        Assert.Null(presence.ItemId);
    }

    [Fact]
    public void LookupGardeSesMessages()
    {
        var vault = Vault(Item("i1", "ntfy"), Item("i2", "dup"), Item("i3", "dup"));

        Assert.Equal(Loc.F("Inject_Error_ItemMissingVault", "absent"), vault.Lookup(new("absent", "x")).Failure);
        Assert.Equal(Loc.F("Inject_Error_ItemAmbiguous", "dup"), vault.Lookup(new("dup", "x")).Failure);
        Assert.Equal(Loc.F("Inject_Error_EmptyField", "ntfy", "x"), vault.Lookup(new("ntfy", "x")).Failure);
    }
}
