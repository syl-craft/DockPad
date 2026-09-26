using System.Globalization;
using DockPad.Secrets;
using DockPad.Services.Localization;

namespace DockPad.Tests.Secrets;

/// <summary>Ce que le formulaire propose, et ce qu'il écrit une fois rempli.</summary>
public class SecretCreationPlanTests
{
    public SecretCreationPlanTests() => Loc.SetCulture(CultureInfo.GetCultureInfo("fr"));

    private static Func<SecretMarker, SecretPresence> Presence(
        params (string Item, string Field, SecretPresenceKind Kind, string? Id)[] table) =>
        m => table.FirstOrDefault(t =>
                string.Equals(t.Item, m.Item, StringComparison.OrdinalIgnoreCase)
             && string.Equals(t.Field, m.Field, StringComparison.OrdinalIgnoreCase)) is { Item: not null } hit
            ? new SecretPresence(hit.Kind, hit.Id)
            : new SecretPresence(SecretPresenceKind.Found, "x");

    [Fact]
    public void GroupeParItem_EtDistingueNouveauEtExistant()
    {
        var plan = SecretCreationPlan.Build(
            [new("ia-requester-infra", "db-password"), new("ia-requester-infra", "api-key"), new("ntfy-infra", "admin-hash")],
            Presence(("ia-requester-infra", "db-password", SecretPresenceKind.ItemMissing, null),
                     ("ia-requester-infra", "api-key", SecretPresenceKind.ItemMissing, null),
                     ("ntfy-infra", "admin-hash", SecretPresenceKind.FieldMissing, "n1")));

        Assert.Equal(2, plan.Count);
        Assert.True(plan[0].IsNew);
        Assert.Equal(new[] { "db-password", "api-key" }, plan[0].Fields);
        Assert.Equal("n1", plan[1].ItemId);
        Assert.Equal(new[] { "admin-hash" }, plan[1].Fields);
    }

    [Fact]
    public void LaCasseDuNomDItem_NeFaitPasDeuxGroupes()
    {
        var plan = SecretCreationPlan.Build(
            [new("NTFY-infra", "a"), new("ntfy-infra", "b")],
            Presence(("ntfy-infra", "a", SecretPresenceKind.ItemMissing, null),
                     ("ntfy-infra", "b", SecretPresenceKind.ItemMissing, null)));

        var only = Assert.Single(plan);
        Assert.Equal("NTFY-infra", only.ItemName);
        Assert.Equal(new[] { "a", "b" }, only.Fields);
    }

    [Fact]
    public void UnMemeChampDemandeDeuxFois_NApparaitQuUneFois()
    {
        var plan = SecretCreationPlan.Build(
            [new("ntfy", "token"), new("ntfy", "TOKEN")],
            Presence(("ntfy", "token", SecretPresenceKind.FieldMissing, "n1")));

        Assert.Equal(new[] { "token" }, Assert.Single(plan).Fields);
    }

    [Fact]
    public void TrouveEtAmbigu_SontExclus()
    {
        var plan = SecretCreationPlan.Build(
            [new("ok", "a"), new("dup", "a")],
            Presence(("dup", "a", SecretPresenceKind.Ambiguous, null)));

        Assert.Empty(plan);
    }

    [Fact]
    public void UnChampLaisseVide_NEstPasCree_EtUnItemSansValeurDisparait()
    {
        var requests = new List<SecretItemRequest>
        {
            new("ia", null, ["db-password", "api-key"]),
            new("vide", null, ["x"]),
        };

        var creations = SecretCreationPlan.WithValues(requests,
            (item, field) => (item, field) switch { ("ia", "db-password") => "p@ss", _ => "" });

        var only = Assert.Single(creations);
        Assert.Equal("ia", only.ItemName);
        Assert.Equal([new SecretFieldValue("db-password", "p@ss")], only.Fields);
    }

    [Fact]
    public void LeMemeChampDuPressePapierEtDUneAnnotation_NeDonneQuUneDemande()
    {
        var entries = new List<ComposeSecret> { new("vw-token", "token", new SecretMarker("ntfy", "token")) };

        var demanded = SecretCreationPlan.Demanded(
            "TOKEN={{ bw:ntfy:token }}", SecretMode.Both, entries, new Dictionary<string, string>());

        Assert.Equal([new SecretMarker("ntfy", "token")], demanded);
    }

    [Fact]
    public void LesMarqueursDesModeles_SontDemandes()
    {
        var entries = new List<ComposeSecret> { new("cfg", "server.yml", null, "templates/server.yml") };
        var templates = new Dictionary<string, string> { ["templates/server.yml"] = "hash: \"{{ bw:ntfy:admin-hash }}\"" };

        var demanded = SecretCreationPlan.Demanded("", SecretMode.Files, entries, templates);

        Assert.Equal([new SecretMarker("ntfy", "admin-hash")], demanded);
    }

    [Fact]
    public void LaCollectionReglee_EstChoisie_ParNomOuParId()
    {
        IReadOnlyList<SecretCollection> collections = [new("c1", "identifiants"), new("c2", "infra")];

        Assert.Equal("c2", SecretCreationPlan.DefaultCollection(collections, "INFRA").Selected!.Id);
        Assert.Equal("c1", SecretCreationPlan.DefaultCollection(collections, "c1").Selected!.Id);
    }

    [Fact]
    public void SansReglage_LaPremiereParOrdreAlphabetique_SansAlerte()
    {
        IReadOnlyList<SecretCollection> collections = [new("c2", "infra"), new("c1", "identifiants")];

        var (selected, missing) = SecretCreationPlan.DefaultCollection(collections, "");

        Assert.Equal("identifiants", selected!.Name);
        Assert.False(missing);
    }

    [Fact]
    public void UnReglageIntrouvable_RetombeEtLeSignale()
    {
        IReadOnlyList<SecretCollection> collections = [new("c2", "infra")];

        var (selected, missing) = SecretCreationPlan.DefaultCollection(collections, "archives");

        Assert.Equal("infra", selected!.Name);
        Assert.True(missing);
    }

    [Fact]
    public void AucuneCollection_RienDeChoisi()
    {
        Assert.Null(SecretCreationPlan.DefaultCollection([], "infra").Selected);
    }
}
