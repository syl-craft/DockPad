using System.Text;
using System.Text.Json.Nodes;
using DockPad.Secrets;

namespace DockPad.Tests.Secrets;

/// <summary>
/// Le piège de <c>bw edit item</c> : il remplace la fiche ENTIÈRE. Ces tests figent qu'on ne perd rien.
/// </summary>
public class BwItemPatchTests
{
    private static JsonNode Parse(string json) => JsonNode.Parse(json)!;

    [Fact]
    public void UnNouvelItem_EstUnIdentifiant_RangeDansSaCollection()
    {
        var item = Parse(BwItemPatch.NewItem("ia-requester-infra", "org1", "col1",
            [new("password", "p"), new("api-key", "k")]));

        Assert.Equal(1, (int)item["type"]!);
        Assert.Equal("ia-requester-infra", (string)item["name"]!);
        Assert.Equal("org1", (string)item["organizationId"]!);
        Assert.Equal("col1", (string)item["collectionIds"]![0]!);
        Assert.Equal("p", (string)item["login"]!["password"]!);

        var field = item["fields"]!.AsArray().Single()!;
        Assert.Equal("api-key", (string)field["name"]!);
        Assert.Equal("k", (string)field["value"]!);
        Assert.Equal(BwItemPatch.HiddenField, (int)field["type"]!);
    }

    [Fact]
    public void SansOrganisation_NiOrganisationNiCollection()
    {
        var item = Parse(BwItemPatch.NewItem("perso", null, null, [new("token", "t")]));

        Assert.Null(item["organizationId"]);
        Assert.Empty(item["collectionIds"]!.AsArray());
    }

    [Fact]
    public void LesNomsStandards_SontInsensiblesALaCasse()
    {
        var item = Parse(BwItemPatch.NewItem("x", null, null,
            [new("Password", "p"), new("USERNAME", "u"), new("Notes", "n"), new("totp", "t")]));

        Assert.Equal("p", (string)item["login"]!["password"]!);
        Assert.Equal("u", (string)item["login"]!["username"]!);
        Assert.Equal("t", (string)item["login"]!["totp"]!);
        Assert.Equal("n", (string)item["notes"]!);
        Assert.Empty(item["fields"]!.AsArray());
    }

    private const string FullItem = """
        {
          "id": "n1", "organizationId": "org1", "collectionIds": ["col1"], "type": 1,
          "name": "ntfy-infra", "notes": null, "favorite": true,
          "login": { "username": "admin", "password": null, "totp": null,
                     "uris": [ { "match": null, "uri": "https://ntfy.example" } ] },
          "fields": [ { "name": "token", "value": "", "type": 1 }, { "name": "keep", "value": "k", "type": 0 } ],
          "passwordHistory": [ { "password": "old", "lastUsedDate": "2026-01-01" } ],
          "unknownFutureProperty": { "nested": 42 }
        }
        """;

    [Fact]
    public void CompleterUneFiche_NePerdRien()
    {
        var item = Parse(BwItemPatch.AddFields(FullItem, [new("admin-hash", "h")]));

        Assert.Equal("https://ntfy.example", (string)item["login"]!["uris"]![0]!["uri"]!);
        Assert.Equal("old", (string)item["passwordHistory"]![0]!["password"]!);
        Assert.Equal(42, (int)item["unknownFutureProperty"]!["nested"]!);
        Assert.True((bool)item["favorite"]!);
        Assert.Equal("k", (string)item["fields"]![1]!["value"]!);
        Assert.Equal("admin-hash", (string)item["fields"]![2]!["name"]!);
        Assert.Equal(BwItemPatch.HiddenField, (int)item["fields"]![2]!["type"]!);
    }

    [Fact]
    public void UnChampPersonnaliseVide_EstRempliEnPlace()
    {
        var item = Parse(BwItemPatch.AddFields(FullItem, [new("TOKEN", "tk")]));

        var fields = item["fields"]!.AsArray();
        Assert.Equal(2, fields.Count);
        Assert.Equal("tk", (string)fields[0]!["value"]!);
    }

    [Fact]
    public void UnChampPersonnaliseNommePassword_GagneSurLeStandard()
    {
        const string json = """{ "type": 1, "name": "x", "login": { "password": null }, "fields": [ { "name": "password", "value": "", "type": 1 } ] }""";

        var item = Parse(BwItemPatch.AddFields(json, [new("password", "p")]));

        Assert.Equal("p", (string)item["fields"]![0]!["value"]!);
        Assert.Null(item["login"]!["password"]);
    }

    [Fact]
    public void UneFicheSansLogin_RecoitUnLogin()
    {
        const string json = """{ "type": 2, "name": "note", "secureNote": { "type": 0 } }""";

        var item = Parse(BwItemPatch.AddFields(json, [new("password", "p")]));

        Assert.Equal("p", (string)item["login"]!["password"]!);
        Assert.Equal(0, (int)item["secureNote"]!["type"]!);
    }

    [Fact]
    public void UneFicheSansChamps_RecoitSonTableau()
    {
        const string json = """{ "type": 1, "name": "x", "fields": null }""";

        var item = Parse(BwItemPatch.AddFields(json, [new("token", "t")]));

        Assert.Equal("t", (string)item["fields"]![0]!["value"]!);
    }

    [Fact]
    public void LEncodage_EstFidele_PourLesCaracteresDifficiles()
    {
        const string value = "$$2y$$10$$é\"\\ü";

        var json = BwItemPatch.NewItem("x", null, null, [new("hash", value)]);
        var decoded = Encoding.UTF8.GetString(Convert.FromBase64String(BwItemPatch.Encode(json)));

        Assert.Equal(value, (string)Parse(decoded)["fields"]![0]!["value"]!);
    }
}
