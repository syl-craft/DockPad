using DockPad.Secrets;

namespace DockPad.Tests.Secrets;

/// <summary>
/// Extraction d'une propriété JSON : chaîne décodée, nombre et booléen tels qu'écrits, index de
/// tableau ; échec sur null, objet, tableau, chemin absent ou JSON invalide.
/// </summary>
public class SecretJsonSelectorTests
{
    private const string ServiceAccount = """
        {
          "type": "service_account",
          "private_key": "-----BEGIN PRIVATE KEY-----\nMIIE\n-----END PRIVATE KEY-----\n",
          "port": 5432,
          "enabled": true,
          "nothing": null,
          "database": { "password": "pg-secret" },
          "servers": [ { "host": "nas" }, { "host": "vps" } ]
        }
        """;

    [Fact]
    public void UneChaine_EstRendueDecodee()
    {
        var selection = SecretJsonSelector.Select(ServiceAccount, "private_key");

        Assert.Equal("-----BEGIN PRIVATE KEY-----\nMIIE\n-----END PRIVATE KEY-----\n", selection.Value);
        Assert.Equal(SecretJsonFailure.None, selection.Failure);
    }

    [Theory]
    [InlineData("port", "5432")]
    [InlineData("enabled", "true")]
    [InlineData("database.password", "pg-secret")]
    [InlineData("servers.1.host", "vps")]
    public void UnScalaire_EstRenduTelQuEcrit(string path, string expected)
    {
        Assert.Equal(expected, SecretJsonSelector.Select(ServiceAccount, path).Value);
    }

    [Theory]
    [InlineData("nothing")]
    [InlineData("database")]
    [InlineData("servers")]
    [InlineData("absent")]
    [InlineData("servers.7.host")]
    [InlineData("servers.x")]
    [InlineData("type.inner")]
    [InlineData("database..password")]
    public void SansValeurScalaire_LExtractionEchoue(string path)
    {
        var selection = SecretJsonSelector.Select(ServiceAccount, path);

        Assert.Null(selection.Value);
        Assert.Equal(SecretJsonFailure.NoScalarValue, selection.Failure);
    }

    [Theory]
    [InlineData("not json")]
    [InlineData("{ \"a\": ")]
    [InlineData("")]
    public void UnJsonInvalide_EchoueSansRienRendre(string json)
    {
        var selection = SecretJsonSelector.Select(json, "a");

        Assert.Null(selection.Value);
        Assert.Equal(SecretJsonFailure.InvalidJson, selection.Failure);
    }
}
