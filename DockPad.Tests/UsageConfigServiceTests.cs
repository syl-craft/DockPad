using System.IO;
using DockPad.Models;
using DockPad.Services;

namespace DockPad.Tests;

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
        Assert.Equal("Claude Code", claude.DetectedName);
        Assert.False(claude.Hidden);
        Assert.Equal(@"C:\x", claude.DataPath);
        Assert.True(claude.Detected);

        var demo = cfg.Providers[1];
        Assert.Equal("demo", demo.Id);
        Assert.True(demo.Hidden);
        Assert.Equal(1, demo.Order);
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
}
