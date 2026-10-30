using System.IO;
using DockPad.Models;
using DockPad.Services;

namespace DockPad.Tests;

/// <summary>
/// Lecture tolérante, persistance des réglages et notification après sauvegarde réussie, avec isolation des abonnés.
/// </summary>
public class UsageConfigServiceTests : IDisposable
{
    private readonly string _path = Path.Combine(Path.GetTempPath(), $"usage_{Guid.NewGuid():N}.json");
    public void Dispose() { if (File.Exists(_path)) File.Delete(_path); }

    [Fact]
    public void Load_FichierAbsent_RetourneDefauts()
    {
        var cfg = UsageConfigService.Load(_path);

        Assert.True(cfg.Enabled);
        Assert.Equal(15, cfg.AlertThreshold);
        Assert.True(cfg.ShowCost);
        Assert.Equal("", cfg.DefaultProviderId);
        Assert.Empty(cfg.Providers);
    }

    [Fact]
    public void Load_FichierCorrompu_RetourneDefauts()
    {
        File.WriteAllText(_path, "{pas du json");

        var cfg = UsageConfigService.Load(_path);

        Assert.True(cfg.Enabled);
        Assert.Equal(15, cfg.AlertThreshold);
        Assert.Empty(cfg.Providers);
    }

    [Fact]
    public void SavePuisLoad_ConserveLesValeurs()
    {
        UsageConfigService.Save(new UsageConfig
        {
            Enabled = false,
            AlertThreshold = 30,
            ShowCost = false,
            DefaultProviderId = "claude",
        }, _path);

        var cfg = UsageConfigService.Load(_path);

        Assert.False(cfg.Enabled);
        Assert.Equal(30, cfg.AlertThreshold);
        Assert.False(cfg.ShowCost);
        Assert.Equal("claude", cfg.DefaultProviderId);
    }

    [Fact]
    public void Save_NotifieApresChaqueSauvegardeReussie()
    {
        var thresholds = new List<int>();
        Action changed = () => thresholds.Add(UsageConfigService.Load(_path).AlertThreshold);
        UsageConfigService.Changed += changed;
        try
        {
            var config = new UsageConfig { AlertThreshold = 30 };
            UsageConfigService.Save(config, _path);
            UsageConfigService.Save(config, _path);

            Assert.Equal(new[] { 30, 30 }, thresholds);
        }
        finally
        {
            UsageConfigService.Changed -= changed;
        }
    }

    [Fact]
    public void Save_UnAbonneEnErreurNEmpechePasLesSuivants()
    {
        var notifications = 0;
        Action failing = () => throw new InvalidOperationException("Subscriber failed.");
        Action changed = () => notifications++;
        UsageConfigService.Changed += failing;
        UsageConfigService.Changed += changed;
        try
        {
            UsageConfigService.Save(new UsageConfig { AlertThreshold = 30 }, _path);

            Assert.Equal(1, notifications);
            Assert.Equal(30, UsageConfigService.Load(_path).AlertThreshold);
        }
        finally
        {
            UsageConfigService.Changed -= failing;
            UsageConfigService.Changed -= changed;
        }
    }

    [Fact]
    public void Save_EchecDeSauvegardeNeNotifiePas()
    {
        File.WriteAllText(_path, "{invalid json");
        var notifications = 0;
        Action changed = () => notifications++;
        UsageConfigService.Changed += changed;
        try
        {
            Assert.Throws<System.Text.Json.JsonException>(() =>
                UsageConfigService.Save(new UsageConfig(), _path));

            Assert.Equal(0, notifications);
            Assert.Equal("{invalid json", File.ReadAllText(_path));
        }
        finally
        {
            UsageConfigService.Changed -= changed;
        }
    }

    [Fact]
    public void SavePuisLoad_ConserveLesFournisseurs()
    {
        UsageConfigService.Save(new UsageConfig
        {
            Providers =
            {
                new AiProviderEntry { Id = "claude", Name = "Mon Claude", DetectedName = "Claude Code",
                                      Hidden = false, Order = 0, DataPath = @"C:\x", Detected = true },
                new AiProviderEntry { Id = "demo", Name = "Démo", DetectedName = "Démo",
                                      Hidden = true, Order = 1 },
            },
        }, _path);

        var cfg = UsageConfigService.Load(_path);

        Assert.Equal(2, cfg.Providers.Count);
        var claude = cfg.Providers[0];
        Assert.Equal("claude", claude.Id);
        Assert.Equal("Mon Claude", claude.Name);
        Assert.Equal("Claude", claude.DetectedName);
        Assert.False(claude.Hidden);
        Assert.Equal(@"C:\x", claude.DataPath);
        Assert.True(claude.Detected);

        var demo = cfg.Providers[1];
        Assert.Equal("demo", demo.Id);
        Assert.True(demo.Hidden);
        Assert.Equal(1, demo.Order);
    }

    [Fact]
    public void Load_AncienNomClaude_RaccourcitLeLibelle()
    {
        File.WriteAllText(_path, """
        {
          "providers": [
            { "id": "claude", "name": "Claude Code", "detectedName": "Claude Code" }
          ]
        }
        """);

        var provider = Assert.Single(UsageConfigService.Load(_path).Providers);

        Assert.Equal("Claude", provider.Name);
        Assert.Equal("Claude", provider.DetectedName);
    }

    [Fact]
    public void Load_FournisseurSansId_IgnoreLEntreeMaisGardeLeReste()
    {
        // Une entrée sans id est inexploitable — la clé de fusion manque. Le reste du fichier,
        // lui, est parfaitement lisible : le perdre effacerait masquages et ordre de tous les autres.
        File.WriteAllText(_path, """
        {
          "enabled": true,
          "alertThreshold": 25,
          "providers": [
            { "name": "sans id", "order": 0 },
            { "id": "claude", "name": "Claude", "order": 1 }
          ]
        }
        """);

        var cfg = UsageConfigService.Load(_path);

        Assert.Equal(25, cfg.AlertThreshold);
        Assert.Single(cfg.Providers);
        Assert.Equal("claude", cfg.Providers[0].Id);
    }

    [Fact]
    public void Load_IdsEnDoublon_GardeLePremierSansLever()
    {
        // La fusion et l'agrégation indexent cette liste par id : un doublon y lèverait, et
        // l'exception masquerait le bandeau entier. On écarte ici, à la porte d'entrée du fichier.
        File.WriteAllText(_path, """
        {
          "providers": [
            { "id": "claude", "name": "Le bon", "order": 0 },
            { "id": "Claude", "name": "Le doublon", "order": 1 },
            { "id": "demo", "name": "Démo", "order": 2 }
          ]
        }
        """);

        var cfg = UsageConfigService.Load(_path);

        Assert.Equal(2, cfg.Providers.Count);
        Assert.Equal("Le bon", cfg.Providers.Single(p => p.Id == "claude").Name);
    }

    [Fact]
    public void Save_CreeLeDossierManquant()
    {
        var nested = Path.Combine(Path.GetTempPath(), $"usagedir_{Guid.NewGuid():N}", "usage.json");
        try
        {
            UsageConfigService.Save(new UsageConfig(), nested);
            Assert.True(File.Exists(nested));
        }
        finally
        {
            var dir = Path.GetDirectoryName(nested)!;
            if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void Reglages_Absents_Defauts()
    {
        File.WriteAllText(_path, "{\"alertThreshold\":30}");

        var cfg = UsageConfigService.Load(_path);

        Assert.Equal(UsagePlacement.Bottom, cfg.Placement);
        Assert.Equal(SideStripStyle.Columns, cfg.BarStyle);
        Assert.Equal(UsageFocusMode.Manual, cfg.FocusMode);
        Assert.True(cfg.ShowSystemLoad);
        Assert.True(cfg.ShowLoadHistory);
    }

    [Fact]
    public void Reglages_RelusApresSauvegarde()
    {
        UsageConfigService.Save(new UsageConfig
        {
            Placement = UsagePlacement.Right,
            BarStyle = SideStripStyle.Rings,
            FocusMode = UsageFocusMode.AutoClosestToLimit,
            ShowSystemLoad = false,
            ShowLoadHistory = false,
        }, _path);

        var cfg = UsageConfigService.Load(_path);

        Assert.Equal(UsagePlacement.Right, cfg.Placement);
        Assert.Equal(SideStripStyle.Rings, cfg.BarStyle);
        Assert.Equal(UsageFocusMode.AutoClosestToLimit, cfg.FocusMode);
        Assert.False(cfg.ShowSystemLoad);
        Assert.False(cfg.ShowLoadHistory);
    }

    [Fact]
    public void Placement_ChaineEcriteEnTexte()
    {
        UsageConfigService.Save(new UsageConfig { Placement = UsagePlacement.Right }, _path);

        Assert.Contains("\"placement\": \"Right\"", File.ReadAllText(_path));
    }

    private const string Neighbours =
        "\"alertThreshold\":30,\"placement\":\"Right\",\"barStyle\":\"Rings\",\"focusMode\":\"AutoClosestToLimit\"," +
        "\"showSystemLoad\":false,\"showLoadHistory\":false,\"providers\":[{\"id\":\"claude\"}]";

    /// <summary>
    /// Écrit un fichier dont tous les champs valent autre chose que le défaut, sauf <paramref name="field"/>.
    /// </summary>
    private UsageConfig LoadWithInvalid(string field, string invalidJson)
    {
        var others = string.Join(",", Neighbours.Split(",\"")
            .Select(part => part.TrimStart('"'))
            .Where(part => part.StartsWith(field + "\"") == false)
            .Select(part => "\"" + part));
        File.WriteAllText(_path, $"{{{others},\"{field}\":{invalidJson}}}");
        return UsageConfigService.Load(_path);
    }

    private static void AssertOthersKept(UsageConfig cfg, string field)
    {
        Assert.Equal(30, cfg.AlertThreshold);
        Assert.Equal("claude", Assert.Single(cfg.Providers).Id);
        if (field != "placement") Assert.Equal(UsagePlacement.Right, cfg.Placement);
        if (field != "barStyle") Assert.Equal(SideStripStyle.Rings, cfg.BarStyle);
        if (field != "focusMode") Assert.Equal(UsageFocusMode.AutoClosestToLimit, cfg.FocusMode);
        if (field != "showSystemLoad") Assert.False(cfg.ShowSystemLoad);
        if (field != "showLoadHistory") Assert.False(cfg.ShowLoadHistory);
    }

    public static TheoryData<string> InvalidEnumForms => new()
    {
        "null", "true", "\"gauche\"", "7", "\"7\"", "{\"a\":1}", "[1]", "\"\"",
        "\"Bottom, Right\"", "\"Manual, AutoClosestToLimit\"", "\"Columns, Rings\"",
    };

    public static TheoryData<string> InvalidBoolForms => new()
    {
        "null", "\"oui\"", "\"true\"", "0", "7", "{\"a\":1}", "[true]",
    };

    [Theory]
    [MemberData(nameof(InvalidEnumForms))]
    public void Placement_FormesInvalides_EnBas(string json)
    {
        var cfg = LoadWithInvalid("placement", json);

        Assert.Equal(UsagePlacement.Bottom, cfg.Placement);
        AssertOthersKept(cfg, "placement");
    }

    [Theory]
    [MemberData(nameof(InvalidEnumForms))]
    public void FocusMode_FormesInvalides_Manuel(string json)
    {
        var cfg = LoadWithInvalid("focusMode", json);

        Assert.Equal(UsageFocusMode.Manual, cfg.FocusMode);
        AssertOthersKept(cfg, "focusMode");
    }

    [Theory]
    [MemberData(nameof(InvalidEnumForms))]
    public void BarStyle_FormesInvalides_Colonnes(string json)
    {
        var cfg = LoadWithInvalid("barStyle", json);

        Assert.Equal(SideStripStyle.Columns, cfg.BarStyle);
        AssertOthersKept(cfg, "barStyle");
    }

    [Theory]
    [MemberData(nameof(InvalidBoolForms))]
    public void ShowSystemLoad_FormesInvalides_Vrai(string json)
    {
        var cfg = LoadWithInvalid("showSystemLoad", json);

        Assert.True(cfg.ShowSystemLoad);
        AssertOthersKept(cfg, "showSystemLoad");
    }

    [Theory]
    [MemberData(nameof(InvalidBoolForms))]
    public void ShowLoadHistory_FormesInvalides_Vrai(string json)
    {
        var cfg = LoadWithInvalid("showLoadHistory", json);

        Assert.True(cfg.ShowLoadHistory);
        AssertOthersKept(cfg, "showLoadHistory");
    }

    [Fact]
    public void ChampInvalide_SauvegardeEcritUneValeurValide()
    {
        File.WriteAllText(_path, """
            {"alertThreshold":30,"placement":"Bottom, Right","barStyle":"Columns, Rings","focusMode":"gauche",
             "showSystemLoad":"oui","showLoadHistory":7,"providers":[{"id":"claude"}]}
            """);

        UsageConfigService.Save(UsageConfigService.Load(_path), _path);

        var written = File.ReadAllText(_path);
        Assert.Contains("\"placement\": \"Bottom\"", written);
        Assert.Contains("\"focusMode\": \"Manual\"", written);
        Assert.Contains("\"showSystemLoad\": true", written);
        Assert.Contains("\"showLoadHistory\": true", written);
        var reloaded = UsageConfigService.Load(_path);
        Assert.Equal(UsagePlacement.Bottom, reloaded.Placement);
        Assert.Equal(SideStripStyle.Columns, reloaded.BarStyle);
        Assert.Equal(UsageFocusMode.Manual, reloaded.FocusMode);
        Assert.True(reloaded.ShowSystemLoad);
        Assert.True(reloaded.ShowLoadHistory);
        Assert.Equal(30, reloaded.AlertThreshold);
        Assert.Equal("claude", Assert.Single(reloaded.Providers).Id);
    }

    [Theory]
    [InlineData("\"Columns, Rings\"")]
    [InlineData("\"2\"")]
    public void StreamDockSideStrip_NomCompositeOuNumeriqueEnChaine_Colonnes(string json)
    {
        var settings = System.Text.Json.JsonSerializer.Deserialize<AppSettings>($"{{\"streamDockSideStrip\":{json}}}",
            new System.Text.Json.JsonSerializerOptions { PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase });

        Assert.Equal(SideStripStyle.Columns, settings?.StreamDockSideStrip);
    }
}
