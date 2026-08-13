using DockPad.Models;
using DockPad.Services;

namespace DockPad.Tests;

public class BrowserActionServiceTests
{
    private static BrowsersConfig Cfg() => new()
    {
        Browsers =
        [
            new BrowserEntry { Id = "aaaa1111", Name = "Chrome", ExePath = @"C:\chrome.exe", Order = 0 },
            new BrowserEntry { Id = "bbbb2222", Name = "Edge",   ExePath = @"C:\edge.exe",   Order = 1 },
        ],
        Rules = [new BrowserRule { Host = "github.com", BrowserId = "aaaa1111" }],
    };

    [Fact]
    public void UpdateBrowserCore_IdInconnu_Echoue()
    {
        var r = BrowserActionService.UpdateBrowserCore(Cfg(), "zzzz9999", new BrowserUpdate { Name = "X" });
        Assert.False(r.Ok);
    }

    [Fact]
    public void UpdateBrowserCore_AppliqueLesChampsNonNulls()
    {
        var cfg = Cfg();
        var r = BrowserActionService.UpdateBrowserCore(cfg, "aaaa1111",
            new BrowserUpdate { Arguments = "--incognito", Hidden = true });
        Assert.True(r.Ok);
        var b = cfg.Browsers.First(b => b.Id == "aaaa1111");
        Assert.Equal("--incognito", b.Arguments);
        Assert.True(b.Hidden);
        Assert.Equal("Chrome", b.Name); // inchangé
    }

    [Fact]
    public void UpdateBrowserCore_Order_ReindexeToutLeMonde()
    {
        var cfg = Cfg();
        var r = BrowserActionService.UpdateBrowserCore(cfg, "bbbb2222", new BrowserUpdate { Order = 0 });
        Assert.True(r.Ok);
        Assert.Equal(0, cfg.Browsers.First(b => b.Id == "bbbb2222").Order);
        Assert.Equal(1, cfg.Browsers.First(b => b.Id == "aaaa1111").Order);
    }

    /// <summary>Chrome (2 profils) puis Edge.</summary>
    private static BrowsersConfig CfgAvecProfils()
    {
        var cfg = Cfg();
        cfg.Browsers.Insert(1, new BrowserEntry
        {
            Id = "cccc3333", Name = "Boulot", ExePath = @"C:\chrome.exe", Order = 1,
            ParentId = "aaaa1111", ProfileDirectory = "Default",
        });
        cfg.Browsers.Insert(2, new BrowserEntry
        {
            Id = "dddd4444", Name = "Perso", ExePath = @"C:\chrome.exe", Order = 2,
            ParentId = "aaaa1111", ProfileDirectory = "Profile 1",
        });
        cfg.Browsers.First(b => b.Id == "bbbb2222").Order = 3;
        return cfg;
    }

    private static List<string> DisplayOrder(BrowsersConfig cfg) =>
        BrowserRowLayout.Grouped(cfg).Select(r => r.Entry.Name).ToList();

    [Fact]
    public void UpdateBrowserCore_OrderDUnProfil_CompteParmiSesFreresPasDansTouteLaListe()
    {
        var cfg = CfgAvecProfils();
        // Perso est déjà le 2e profil de Chrome : order 1 ne doit rien changer.
        var r = BrowserActionService.UpdateBrowserCore(cfg, "dddd4444", new BrowserUpdate { Order = 1 });
        Assert.True(r.Ok);
        Assert.Equal(["Chrome", "Boulot", "Perso", "Edge"], DisplayOrder(cfg));
    }

    [Fact]
    public void UpdateBrowserCore_OrderDUnNavigateur_ComptteParmiLesNavigateurs()
    {
        var cfg = CfgAvecProfils();
        // Chrome en 2e navigateur : ses profils le suivent, derrière Edge.
        var r = BrowserActionService.UpdateBrowserCore(cfg, "aaaa1111", new BrowserUpdate { Order = 1 });
        Assert.True(r.Ok);
        Assert.Equal(["Edge", "Chrome", "Boulot", "Perso"], DisplayOrder(cfg));
    }

    [Fact]
    public void ListBrowsersCore_ExposeLeProfilEtSonParent()
    {
        var cfg = Cfg();
        cfg.Browsers.Add(new BrowserEntry
        {
            Id = "cccc3333", Name = "Boulot", ExePath = @"C:\chrome.exe", Order = 2,
            ParentId = "aaaa1111", ProfileDirectory = "Default",
        });

        var json = System.Text.Json.JsonSerializer.Serialize(BrowserActionService.ListBrowsersCore(cfg).Data);

        Assert.Contains("\"ParentId\":\"aaaa1111\"", json);
        Assert.Contains("\"ProfileDirectory\":\"Default\"", json);
    }

    [Fact]
    public void ListBrowsersCore_ListeLesProfilsJusteApresLeurNavigateur()
    {
        var cfg = Cfg();
        cfg.Browsers.Add(new BrowserEntry
        {
            Id = "cccc3333", Name = "Boulot", ExePath = @"C:\chrome.exe", Order = 2,
            ParentId = "aaaa1111", ProfileDirectory = "Default",
        });

        var json = System.Text.Json.JsonSerializer.Serialize(BrowserActionService.ListBrowsersCore(cfg).Data);

        Assert.True(json.IndexOf("Boulot") < json.IndexOf("Edge"),
                    "le profil doit suivre Chrome, pas se retrouver en fin de liste");
    }

    [Fact]
    public void AddRuleCore_HostDejaRegle_Echoue()
    {
        var r = BrowserActionService.AddRuleCore(Cfg(), "GitHub.com", "bbbb2222"); // insensible à la casse
        Assert.False(r.Ok);
        Assert.Contains("Chrome", r.Error);
    }

    [Fact]
    public void AddRuleCore_BrowserInconnu_Echoue()
    {
        var r = BrowserActionService.AddRuleCore(Cfg(), "example.com", "zzzz9999");
        Assert.False(r.Ok);
    }

    [Fact]
    public void AddRuleCore_Valide_AjouteEnMinuscules()
    {
        var cfg = Cfg();
        var r = BrowserActionService.AddRuleCore(cfg, " Example.COM ", "bbbb2222");
        Assert.True(r.Ok);
        Assert.Contains(cfg.Rules, x => x is { Host: "example.com", BrowserId: "bbbb2222" });
    }

    [Fact]
    public void DeleteRuleCore_HostInconnu_Echoue()
    {
        var r = BrowserActionService.DeleteRuleCore(Cfg(), "nope.com");
        Assert.False(r.Ok);
    }

    [Fact]
    public void DeleteRuleCore_Supprime()
    {
        var cfg = Cfg();
        var r = BrowserActionService.DeleteRuleCore(cfg, "github.com");
        Assert.True(r.Ok);
        Assert.Empty(cfg.Rules);
    }
}
