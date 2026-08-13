using System.IO;
using DockPad.Services;

namespace DockPad.Tests;

public class AppPathsTests
{
    private const string AppData = @"C:\Users\X\AppData\Roaming";

    [Fact]
    public void Resolve_SansSurcharge_SousDossierDockPadDansAppData()
    {
        Assert.Equal(Path.Combine(AppData, "DockPad"), AppPaths.Resolve(null, AppData));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Resolve_SurchargeVide_RetombeSurAppData(string value)
    {
        Assert.Equal(Path.Combine(AppData, "DockPad"), AppPaths.Resolve(value, AppData));
    }

    [Fact]
    public void Resolve_Surcharge_UtiliseeTelleQuelleSansSousDossier()
    {
        Assert.Equal(@"D:\fixtures\demo", AppPaths.Resolve(@"D:\fixtures\demo", AppData));
    }

    [Fact]
    public void Resolve_SurchargeAvecGuillemetsEtEspaces_EstNettoyee()
    {
        Assert.Equal(@"D:\fixtures\demo", AppPaths.Resolve("  \"D:\\fixtures\\demo\"  ", AppData));
    }

    [Fact]
    public void Resolve_SurchargeRelative_DevientAbsolue()
    {
        Assert.Equal(Path.GetFullPath("fixture-relative"), AppPaths.Resolve("fixture-relative", AppData));
    }

    [Fact]
    public void File_CombineAvecLeProfil()
    {
        Assert.Equal(Path.Combine(AppPaths.ProfileRoot, "browsers.json"), AppPaths.File("browsers.json"));
    }

    [Fact]
    public void ProfileRoot_TermineParDockPad_QuandAucuneSurchargeNEstDefinie()
    {
        // Le test tourne sans DOCKPAD_PROFILE_DIR : le profil doit rester celui de l'utilisateur.
        Assert.Null(Environment.GetEnvironmentVariable(AppPaths.OverrideVariable));
        Assert.Equal("DockPad", Path.GetFileName(AppPaths.ProfileRoot));
    }
}
