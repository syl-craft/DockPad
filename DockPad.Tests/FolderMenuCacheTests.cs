using System.Threading;
using System.Threading.Tasks;
using DockPad.Models;
using DockPad.Services;

namespace DockPad.Tests;

public class FolderMenuCacheTests
{
    [Fact]
    public async Task CachePret_EviteLaLectureSynchrone_EtSeRafraichit()
    {
        var reads = 0;
        var cache = new FolderMenuCache(() => [new("entrée " + Interlocked.Increment(ref reads), "commande %V", null)]);
        Assert.Null(cache.ReadyEntries);
        await cache.RefreshAsync();
        Assert.Equal("entrée 1", Assert.Single(cache.ReadyEntries ?? []).DisplayName);
        Assert.Equal(1, reads);
        await cache.RefreshAsync();
        Assert.Equal("entrée 2", Assert.Single(cache.ReadyEntries ?? []).DisplayName);
    }

    [Fact]
    public async Task InvalidationRegistre_ImmediatementVisible_AncienneLectureNonRepubliee()
    {
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var finish = new ManualResetEventSlim();
        var cache = new FolderMenuCache(() =>
        {
            started.SetResult();
            Assert.True(finish.Wait(TimeSpan.FromSeconds(10)));
            return [new("ancienne", "commande", null)];
        });
        var refresh = cache.RefreshAsync();
        await started.Task;
        try
        {
            RegistryService.InvalidateMenus();
            Assert.Null(cache.ReadyEntries);
        }
        finally { finish.Set(); }
        await refresh;
        Assert.Null(cache.ReadyEntries);
    }

    [Fact]
    public async Task InvalidationRegistre_RetireAussiUnCacheDejaPret()
    {
        var cache = new FolderMenuCache(() => [new("entrée", "commande", null)]);
        await cache.RefreshAsync();
        Assert.NotNull(cache.ReadyEntries);
        RegistryService.InvalidateMenus();
        Assert.Null(cache.ReadyEntries);
        await cache.RefreshAsync();
        Assert.NotNull(cache.ReadyEntries);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task SaveEtDelete_InvalidentAussiLorsDUnEchec(bool delete)
    {
        var cache = new FolderMenuCache(() => [new("entrée", "commande", null)]);
        await cache.RefreshAsync();
        Assert.NotNull(cache.ReadyEntries);
        // Le nom trop long est rejeté par .NET avant toute écriture : le test n'altère aucun menu Windows.
        var entry = new ContextMenuEntry { RegistryKey = new string('x', 256), Target = ContextMenuTarget.FolderBackground };
        if (delete) Assert.Throws<ArgumentException>(() => RegistryService.Delete(entry));
        else Assert.Throws<ArgumentException>(() => RegistryService.Save(entry));
        Assert.Null(cache.ReadyEntries);
    }
}
