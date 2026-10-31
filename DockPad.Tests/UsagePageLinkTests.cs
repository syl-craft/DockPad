using DockPad.Services.Usage;

namespace DockPad.Tests;

/// <summary>
/// Seules les adresses web s'ouvrent : un chemin ou une commande lu quelque part ne doit jamais atteindre le shell.
/// </summary>
public class UsagePageLinkTests
{
    [Theory]
    [InlineData("https://claude.ai/new#settings/usage")]
    [InlineData("http://exemple.fr/usage")]
    public void AdresseWeb_Ouvrable(string url)
    {
        Assert.True(UsagePageLink.IsWebUrl(url, out var uri));
        Assert.Equal(url.StartsWith("https") ? "https" : "http", uri?.Scheme);
    }

    [Theory]
    [InlineData("")]
    [InlineData(null)]
    [InlineData("calc.exe")]
    [InlineData(@"C:\Windows\System32\cmd.exe")]
    [InlineData("file:///C:/Windows/System32/cmd.exe")]
    [InlineData("ms-settings:network")]
    public void PasUneAdresseWeb_Refusee(string? url)
    {
        Assert.False(UsagePageLink.IsWebUrl(url, out var uri));
        Assert.Null(uri);
    }
}
