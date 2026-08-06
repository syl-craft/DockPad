using System.IO;
using DockPad.Models;
using DockPad.Services;

namespace DockPad.Tests;

public class McpConfigServiceTests : IDisposable
{
    private readonly string _path = Path.Combine(Path.GetTempPath(), $"mcp_{Guid.NewGuid():N}.json");
    public void Dispose() { if (File.Exists(_path)) File.Delete(_path); }

    [Fact]
    public void Load_FichierAbsent_RetourneDefauts()
    {
        var cfg = McpConfigService.Load(_path);
        Assert.True(cfg.Enabled);
        Assert.False(cfg.AllowDelete);
    }

    [Fact]
    public void Load_FichierCorrompu_RetourneDefauts()
    {
        File.WriteAllText(_path, "{pas du json");
        var cfg = McpConfigService.Load(_path);
        Assert.True(cfg.Enabled);
        Assert.False(cfg.AllowDelete);
    }

    [Fact]
    public void SavePuisLoad_ConserveLesValeurs()
    {
        McpConfigService.Save(new McpConfig { Enabled = false, AllowDelete = true }, _path);
        var cfg = McpConfigService.Load(_path);
        Assert.False(cfg.Enabled);
        Assert.True(cfg.AllowDelete);
    }
}
