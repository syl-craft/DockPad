using DockPad.Models;
using DockPad.Services;

namespace DockPad.Tests;

public class UrlRouterServiceTests
{
    private const string Url = "https://example.test/a?b=1";

    [Fact]
    public void BuildArguments_SansArguments_AjouteLUrlEntreGuillemets()
    {
        var b = new BrowserEntry { ExePath = @"C:\chrome.exe" };
        Assert.Equal($"\"{Url}\"", UrlRouterService.BuildArguments(b, Url));
    }

    [Fact]
    public void BuildArguments_ArgumentsSansJoker_AjouteLUrlEnFin()
    {
        var b = new BrowserEntry { ExePath = @"C:\chrome.exe", Arguments = "--incognito" };
        Assert.Equal($"--incognito \"{Url}\"", UrlRouterService.BuildArguments(b, Url));
    }

    [Fact]
    public void BuildArguments_JokerPourcentUn_EstSubstitue()
    {
        var b = new BrowserEntry { ExePath = @"C:\chrome.exe", Arguments = "--app=%1 --new-window" };
        Assert.Equal($"--app=\"{Url}\" --new-window", UrlRouterService.BuildArguments(b, Url));
    }

    [Fact]
    public void BuildArguments_Profil_PrefixeProfileDirectory()
    {
        var b = new BrowserEntry { ExePath = @"C:\chrome.exe", ProfileDirectory = "Profile 1" };
        Assert.Equal($"--profile-directory=\"Profile 1\" \"{Url}\"", UrlRouterService.BuildArguments(b, Url));
    }

    [Fact]
    public void BuildArguments_Profil_PrefixeAvantLesArgumentsDeLUtilisateur()
    {
        var b = new BrowserEntry { ExePath = @"C:\chrome.exe", ProfileDirectory = "Default", Arguments = "--incognito" };
        Assert.Equal($"--profile-directory=\"Default\" --incognito \"{Url}\"",
                     UrlRouterService.BuildArguments(b, Url));
    }

    [Fact]
    public void BuildArguments_ProfilEtJoker_GardeLaPositionDuJoker()
    {
        var b = new BrowserEntry { ExePath = @"C:\chrome.exe", ProfileDirectory = "Profile 2", Arguments = "--app=%1" };
        Assert.Equal($"--profile-directory=\"Profile 2\" --app=\"{Url}\"",
                     UrlRouterService.BuildArguments(b, Url));
    }
}
