using DockPad.Models;

namespace DockPad.Tests;

public class SmokeTests
{
    [Fact]
    public void BrowserEntry_NewId_Renvoie8Hex()
    {
        var id = BrowserEntry.NewId();
        Assert.Equal(8, id.Length);
        Assert.All(id, c => Assert.True(Uri.IsHexDigit(c)));
    }
}
