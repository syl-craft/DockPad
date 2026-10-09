using DockPad.Secrets;

namespace DockPad.Tests.Secrets;

/// <summary>
/// Lecture du champ d'un marqueur : un champ, une pièce jointe <c>@nom</c>, un sélecteur
/// <c>|json:chemin</c> en plus, ou un filtre inconnu.
/// </summary>
public class SecretFieldReferenceTests
{
    [Fact]
    public void UnNomDeChamp_EstUnChampSimple()
    {
        var reference = SecretFieldReference.Parse("password");

        Assert.Equal("password", reference.Name);
        Assert.False(reference.IsAttachment);
        Assert.Null(reference.JsonPath);
        Assert.True(reference.IsPlain);
    }

    [Fact]
    public void UnArobaseDesigneUnePieceJointe()
    {
        var reference = SecretFieldReference.Parse("@a.json");

        Assert.Equal("a.json", reference.Name);
        Assert.True(reference.IsAttachment);
        Assert.Null(reference.JsonPath);
        Assert.False(reference.IsPlain);
    }

    [Fact]
    public void UnSelecteurJson_SAjouteAUnePieceJointe()
    {
        var reference = SecretFieldReference.Parse("@a.json|json:k");

        Assert.Equal("a.json", reference.Name);
        Assert.True(reference.IsAttachment);
        Assert.Equal("k", reference.JsonPath);
    }

    [Fact]
    public void UnSelecteurJson_SAjouteAuxNotes()
    {
        var reference = SecretFieldReference.Parse("notes|json:a.0.b");

        Assert.Equal("notes", reference.Name);
        Assert.False(reference.IsAttachment);
        Assert.Equal("a.0.b", reference.JsonPath);
        Assert.False(reference.IsPlain);
    }

    [Fact]
    public void LeNomDePieceJointeGardeSaCasse_LaComparaisonSeFaitAuCoffre()
    {
        Assert.Equal("A.JSON", SecretFieldReference.Parse("@A.JSON").Name);
    }

    [Theory]
    [InlineData("@a.json|yaml:k")]
    [InlineData("token|base64")]
    [InlineData("token|json:")]
    public void UnFiltreInconnuOuVide_EstSignale(string field)
    {
        var reference = SecretFieldReference.Parse(field);

        Assert.True(reference.HasUnknownFilter);
        Assert.False(reference.IsPlain);
    }

    [Fact]
    public void LeMarqueurExposeSaReference()
    {
        var marker = new SecretMarker("infra", "@config.json|json:database.password");

        Assert.Equal("config.json", marker.Reference.Name);
        Assert.Equal("database.password", marker.Reference.JsonPath);
    }
}
