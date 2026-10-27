using System.IO;
using DockPad.Models;
using DockPad.Services;

namespace DockPad.Tests.StreamDock;

/// <summary>
/// Réglages désactivés par défaut, y compris dans un ancien fichier, et persistance des deux options indépendantes.
/// </summary>
public sealed class StreamDockSettingsTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "dockpad-streamdock-settings-" + Guid.NewGuid().ToString("N"));
    private string SettingsPath => Path.Combine(_directory, "settings.json");

    public StreamDockSettingsTests() => Directory.CreateDirectory(_directory);

    public void Dispose()
    {
        try { Directory.Delete(_directory, recursive: true); }
        catch (IOException) { }
    }

    [Fact]
    public void Reglages_DesactivesParDefaut()
    {
        var defaults = new AppSettings();
        Assert.False(defaults.StreamDockEnabled);
        Assert.False(defaults.StreamDockSyncPages);

        File.WriteAllText(SettingsPath, """{ "language": "fr" }""");
        var settings = AppSettingsService.LoadFrom(SettingsPath, registry: _ => null);

        Assert.False(settings.StreamDockEnabled);
        Assert.False(settings.StreamDockSyncPages);
        Assert.Equal("fr", settings.Language);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public void Reglages_RelusApresSauvegarde(bool enabled, bool syncPages)
    {
        AppSettingsService.SaveTo(SettingsPath, new AppSettings
        {
            StreamDockEnabled = !enabled,
            StreamDockSyncPages = !syncPages,
        });
        var settings = AppSettingsService.LoadFrom(SettingsPath, registry: _ => null);
        settings.StreamDockEnabled = enabled;
        settings.StreamDockSyncPages = syncPages;
        AppSettingsService.SaveTo(SettingsPath, settings);

        var reloaded = AppSettingsService.LoadFrom(SettingsPath, registry: _ => null);

        Assert.Equal(enabled, reloaded.StreamDockEnabled);
        Assert.Equal(syncPages, reloaded.StreamDockSyncPages);
    }
}
