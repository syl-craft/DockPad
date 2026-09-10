using System.IO;
using System.Text.Json;
using DockPad.Models;
using DockPad.Services;

namespace DockPad.Tests;

public class JsonConfigFileTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "dockpad-json-" + Guid.NewGuid().ToString("N"));
    private string Path_ => Path.Combine(_dir, "config.json");
    public JsonConfigFileTests() => Directory.CreateDirectory(_dir);
    public void Dispose() => Directory.Delete(_dir, recursive: true);

    [Theory]
    [InlineData("shortcuts")]
    [InlineData("pages")]
    [InlineData("settings")]
    [InlineData("mcp")]
    [InlineData("usage")]
    public void CorruptConfig_CannotBeOverwrittenByFallback(string kind)
    {
        File.WriteAllText(Path_, "{broken");
        Action save = kind switch
        {
            "shortcuts" => Capture(ShortcutService.Load(Path_), v => ShortcutService.Save(v, Path_)),
            "pages" => Capture(PageConfigService.Load(Path_), v => PageConfigService.Save(v, Path_)),
            "settings" => Capture(AppSettingsService.LoadFrom(Path_, _ => null), v => AppSettingsService.SaveTo(Path_, v)),
            "mcp" => Capture(McpConfigService.Load(Path_), v => McpConfigService.Save(v, Path_)),
            _ => Capture(UsageConfigService.Load(Path_), v => UsageConfigService.Save(v, Path_)),
        };
        Assert.Throws<IOException>(save);
        Assert.Equal("{broken", File.ReadAllText(Path_));
    }

    private static Action Capture<T>(T value, Action<T> save) => () => save(value);

    [Fact]
    public void TransientReadFailure_BlocksSaveUntilSuccessfulReload()
    {
        ShortcutService.Save([new ShortcutEntry { Name = "keep" }], Path_);
        List<ShortcutEntry> fallback;
        using (var held = new FileStream(Path_, FileMode.Open, FileAccess.Read, FileShare.None))
            fallback = ShortcutService.Load(Path_);

        Assert.Empty(fallback);
        Assert.Throws<IOException>(() => ShortcutService.Save(fallback, Path_));
        var reloaded = ShortcutService.Load(Path_);
        Assert.Equal("keep", Assert.Single(reloaded).Name);
        reloaded[0].Name = "updated";
        ShortcutService.Save(reloaded, Path_);
        Assert.Equal("updated", Assert.Single(ShortcutService.Load(Path_)).Name);
    }

    [Theory]
    [InlineData("{broken")]
    [InlineData("null")]
    public void SaveWithoutLoad_ProtectsInvalidExistingConfig(string contents)
    {
        File.WriteAllText(Path_, contents);
        Assert.Throws<JsonException>(() => ShortcutService.Save([], Path_));
        Assert.Equal(contents, File.ReadAllText(Path_));
    }

    [Fact]
    public void FailedReplacement_PreservesOriginalAndRemovesTemp()
    {
        ShortcutService.Save([new ShortcutEntry { Name = "keep" }], Path_);
        var original = File.ReadAllBytes(Path_);
        // Autorise la lecture et bloque le remplacement du fichier.
        using (var held = new FileStream(Path_, FileMode.Open, FileAccess.Read, FileShare.Read))
        {
            var error = Record.Exception(() => ShortcutService.Save([], Path_));
            Assert.True(error is IOException or UnauthorizedAccessException, error?.ToString());
        }

        Assert.Equal(original, File.ReadAllBytes(Path_));
        Assert.Equal([Path_], Directory.GetFiles(_dir));
    }

    [Fact]
    public void RepairedConfig_CanBeReloadedAndSaved()
    {
        File.WriteAllText(Path_, "broken");
        ShortcutService.Load(Path_);
        File.WriteAllText(Path_, "[]");
        var restored = ShortcutService.Load(Path_);
        restored.Add(new ShortcutEntry { Name = "restored" });
        ShortcutService.Save(restored, Path_);
        Assert.Equal("restored", Assert.Single(ShortcutService.Load(Path_)).Name);
    }

    [Fact]
    public void RemovedBrokenSettings_CanBeReloadedAndRecreated()
    {
        File.WriteAllText(Path_, "broken");
        AppSettingsService.LoadFrom(Path_, _ => null);
        File.Delete(Path_);
        var reset = AppSettingsService.LoadFrom(Path_, _ => null);
        reset.Language = "fr";
        AppSettingsService.SaveTo(Path_, reset);
        Assert.Equal("fr", AppSettingsService.LoadFrom(Path_, _ => null).Language);
    }
}
