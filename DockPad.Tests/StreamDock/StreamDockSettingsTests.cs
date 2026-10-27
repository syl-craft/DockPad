using System.IO;
using DockPad.Models;
using DockPad.Services;

namespace DockPad.Tests.StreamDock;

/// <summary>
/// Réglages désactivés par défaut et persistance des options indépendantes.
/// Rendu Colonnes par défaut, relecture des quatre rendus et repli des valeurs inconnues sans perdre les autres réglages.
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

    [Fact]
    public void Rendu_DefautColonnes()
    {
        Assert.Equal(SideStripStyle.Columns, new AppSettings().StreamDockSideStrip);

        File.WriteAllText(SettingsPath, """{ "language": "fr" }""");
        var settings = AppSettingsService.LoadFrom(SettingsPath, registry: _ => null);

        Assert.Equal(SideStripStyle.Columns, settings.StreamDockSideStrip);
        Assert.Equal("fr", settings.Language);
    }

    [Fact]
    public void StyleDesTouches_DefautNeon()
    {
        Assert.Equal(DockKeyStyle.Neon, new AppSettings().StreamDockKeyStyle);

        File.WriteAllText(SettingsPath, """{ "language": "fr" }""");
        var settings = AppSettingsService.LoadFrom(SettingsPath, registry: _ => null);

        Assert.Equal(DockKeyStyle.Neon, settings.StreamDockKeyStyle);
    }

    [Theory]
    [InlineData(DockKeyStyle.Neon)]
    [InlineData(DockKeyStyle.Classic)]
    [InlineData(DockKeyStyle.Flat)]
    public void StyleDesTouches_RelueApresSauvegarde(DockKeyStyle style)
    {
        AppSettingsService.SaveTo(SettingsPath, new AppSettings { Language = "fr", StreamDockKeyStyle = style });

        var settings = AppSettingsService.LoadFrom(SettingsPath, registry: _ => null);

        Assert.Equal(style, settings.StreamDockKeyStyle);
        Assert.Equal("fr", settings.Language);
    }

    [Theory]
    [InlineData("999")]
    [InlineData("\"FutureStyle\"")]
    [InlineData("null")]
    [InlineData("\"classic\"")]
    public void StyleDesTouches_ValeurInconnueOuTexte(string value)
    {
        File.WriteAllText(SettingsPath, $$"""
            {
              "language": "fr",
              "streamDockKeyStyle": {{value}},
              "clipboardClearSeconds": 123
            }
            """);

        var settings = AppSettingsService.LoadFrom(SettingsPath, registry: _ => null);

        Assert.Equal(value == "\"classic\"" ? DockKeyStyle.Classic : DockKeyStyle.Neon, settings.StreamDockKeyStyle);
        Assert.Equal(123, settings.ClipboardClearSeconds);
    }

    [Theory]
    [InlineData(SideStripStyle.Columns)]
    [InlineData(SideStripStyle.HeroStack)]
    [InlineData(SideStripStyle.Rings)]
    [InlineData(SideStripStyle.Reservoir)]
    public void Rendu_RelueApresSauvegarde(SideStripStyle style)
    {
        AppSettingsService.SaveTo(SettingsPath, new AppSettings
        {
            Language = "fr",
            StreamDockEnabled = true,
            StreamDockSyncPages = true,
            StreamDockSideStrip = style,
        });

        var settings = AppSettingsService.LoadFrom(SettingsPath, registry: _ => null);

        Assert.Equal(style, settings.StreamDockSideStrip);
        Assert.Equal("fr", settings.Language);
        Assert.True(settings.StreamDockEnabled);
        Assert.True(settings.StreamDockSyncPages);
    }

    [Theory]
    [InlineData("999")]
    [InlineData("-1")]
    [InlineData("\"FutureStyle\"")]
    [InlineData("\"999\"")]
    [InlineData("null")]
    [InlineData("{ \"futureStyle\": true }")]
    public void Rendu_ValeurInconnue_Colonnes(string value)
    {
        File.WriteAllText(SettingsPath, $$"""
            {
              "language": "fr",
              "streamDockEnabled": true,
              "streamDockSideStrip": {{value}},
              "streamDockSyncPages": true,
              "clipboardClearSeconds": 123
            }
            """);

        var settings = AppSettingsService.LoadFrom(SettingsPath, registry: _ => null);

        Assert.Equal(SideStripStyle.Columns, settings.StreamDockSideStrip);
        Assert.Equal("fr", settings.Language);
        Assert.True(settings.StreamDockEnabled);
        Assert.True(settings.StreamDockSyncPages);
        Assert.Equal(123, settings.ClipboardClearSeconds);
        Assert.False(JsonConfigFile.IsReadOnly(SettingsPath));
    }
}
