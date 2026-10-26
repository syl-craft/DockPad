using System.IO;
using System.Runtime.Loader;
using DockPad.Services.StreamDock;
using DockPad.Surfaces;

namespace DockPad.Tests;

/// <summary>
/// Pilote absent ou DLL sans implémentation : erreur sans exception ; pilote factice : contrat partagé avec l'hôte.
/// Les chargements répétés et concurrents conservent la même instance dans un contexte dédié non déchargeable.
/// </summary>
public class SurfaceDriverLoaderTests
{
    private static readonly string TestPluginsFolder = Path.Combine(AppContext.BaseDirectory, "TestPlugins");

    [Fact]
    public void PiloteAbsent_RenvoieUneErreurSansException()
    {
        var result = SurfaceDriverLoader.Load("Missing", TestPluginsFolder);

        Assert.Null(result.Driver);
        Assert.False(string.IsNullOrWhiteSpace(result.Error));
    }

    [Fact]
    public void PiloteFactice_EstChargeEtPartageLeContrat()
    {
        var result = SurfaceDriverLoader.Load("FakeDriver", TestPluginsFolder);

        Assert.Null(result.Error);
        Assert.True(result.Driver is IControlSurfaceDriver);
        var driver = Assert.IsAssignableFrom<IControlSurfaceDriver>(result.Driver);
        Assert.NotEqual(typeof(SurfaceDriverLoaderTests).Assembly, driver.GetType().Assembly);
        Assert.Equal("FakeDriver", driver.Name);
        var context = AssemblyLoadContext.GetLoadContext(driver.GetType().Assembly);
        Assert.NotNull(context);
        Assert.NotSame(AssemblyLoadContext.Default, context);
        Assert.False(context.IsCollectible);
    }

    [Fact]
    public void DllSansPilote_RenvoieUneErreur()
    {
        Assert.True(File.Exists(Path.Combine(TestPluginsFolder, "Empty", "DockPad.Empty.dll")));

        var result = SurfaceDriverLoader.Load("Empty", TestPluginsFolder);

        Assert.Null(result.Driver);
        Assert.Contains("No public control surface driver", result.Error);
    }

    [Fact]
    public void DeuxChargements_MemeInstance()
    {
        var first = SurfaceDriverLoader.Load("FakeDriver", TestPluginsFolder);
        var second = SurfaceDriverLoader.Load("FakeDriver", TestPluginsFolder);

        Assert.NotNull(first.Driver);
        Assert.Null(first.Error);
        Assert.Null(second.Error);
        Assert.Same(first.Driver, second.Driver);
    }

    [Fact]
    public async Task ChargementsConcurrents_MemeInstance()
    {
        var pluginsFolder = Path.Combine(AppContext.BaseDirectory, "TestPluginsConcurrent");
        var results = await Task.WhenAll(Enumerable.Range(0, 16)
            .Select(_ => Task.Run(() => SurfaceDriverLoader.Load("FakeDriver", pluginsFolder))));

        Assert.NotNull(results[0].Driver);
        var otherFolder = SurfaceDriverLoader.Load("FakeDriver", TestPluginsFolder);
        Assert.NotNull(otherFolder.Driver);
        Assert.NotSame(otherFolder.Driver, results[0].Driver);
        Assert.All(results, result =>
        {
            Assert.Null(result.Error);
            Assert.Same(results[0].Driver, result.Driver);
        });
    }
}
