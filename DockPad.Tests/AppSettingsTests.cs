using System.IO;
using System.Text.Json;
using DockPad.Models;
using DockPad.Services;

namespace DockPad.Tests;

/// <summary>
/// Options de l'application dans un fichier, et reprise de celles restées dans le registre.
/// </summary>
/// <remarks>
/// Aucun test ne lit ni n'écrit le vrai registre : la reprise prend un <b>lecteur injectable</b>,
/// ce qui la rend vérifiable sans toucher aux réglages de la machine — et sans dépendre de ce
/// qu'elle contient au moment du test.
/// </remarks>
public class AppSettingsTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "dockpad-settings-" + Guid.NewGuid().ToString("N"));

    public AppSettingsTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch (IOException) { }
    }

    private string File_ => Path.Combine(_dir, "settings.json");

    // ---------------------------------------------------------------- lecture / écriture

    [Fact]
    public void SavePuisLoad_ConserveTout()
    {
        var written = new AppSettings
        {
            Language = "fr", Theme = "Dark",
            TriggerFirst = "Ctrl", TriggerSecond = "Alt",
            ClaudeArgs = "--enable-auto-mode",
            AutoFavicon = false,
            HotkeyModifiers = 6, HotkeyKey = 32,
        };

        AppSettingsService.SaveTo(File_, written);
        var read = AppSettingsService.LoadFrom(File_, registry: _ => null);

        Assert.Equal("fr", read.Language);
        Assert.Equal("Dark", read.Theme);
        Assert.Equal("Ctrl", read.TriggerFirst);
        Assert.Equal("Alt", read.TriggerSecond);
        Assert.Equal("--enable-auto-mode", read.ClaudeArgs);
        Assert.False(read.AutoFavicon);
        Assert.Equal(6, read.HotkeyModifiers);
        Assert.Equal(32, read.HotkeyKey);
    }

    /// <summary>
    /// Un fichier abîmé ne doit pas empêcher l'application de démarrer : elle repart des valeurs
    /// par défaut, comme le font déjà les autres configs du profil.
    /// </summary>
    [Fact]
    public void Load_FichierCorrompu_RendLesDefauts()
    {
        System.IO.File.WriteAllText(File_, "{ ceci n'est pas du JSON");

        var read = AppSettingsService.LoadFrom(File_, registry: _ => null);

        Assert.Equal("", read.Language);
        Assert.True(read.AutoFavicon);
    }

    /// <summary>
    /// Une clé absente du fichier garde sa valeur par défaut, et <c>AutoFavicon</c> vaut
    /// <c>true</c> — un réglage réseau qu'on n'a jamais vu ne peut pas avoir été refusé. Le piège
    /// serait qu'un JSON sans la clé le lise comme « décoché ».
    /// </summary>
    [Fact]
    public void Load_CleAbsente_GardeSonDefaut()
    {
        System.IO.File.WriteAllText(File_, """{ "language": "en" }""");

        var read = AppSettingsService.LoadFrom(File_, registry: _ => null);

        Assert.Equal("en", read.Language);
        Assert.True(read.AutoFavicon);
        Assert.Equal("", read.Theme);
    }

    [Fact]
    public void Load_AutoFaviconExplicitementFaux_RestEFaux()
    {
        System.IO.File.WriteAllText(File_, """{ "autoFavicon": false }""");

        Assert.False(AppSettingsService.LoadFrom(File_, registry: _ => null).AutoFavicon);
    }

    // ---------------------------------------------------------------- reprise du registre

    /// <summary>
    /// Premier démarrage après la migration : le fichier n'existe pas encore, mais les réglages
    /// sont dans le registre. Ils doivent être repris tels quels — sinon l'utilisateur retrouve
    /// une application remise à zéro.
    /// </summary>
    [Fact]
    public void Load_FichierAbsent_ReprendLeRegistre()
    {
        object? Registry(string name) => name switch
        {
            "Language" => "fr",
            "Theme" => "Dark",
            "TriggerFirst" => "Ctrl",
            "TriggerSecond" => "Shift",
            "ClaudeArgs" => "--verbose",
            "AutoFavicon" => 0,
            "HotkeyModifiers" => 6,
            "HotkeyKey" => 77,
            _ => null,
        };

        var read = AppSettingsService.LoadFrom(File_, Registry);

        Assert.Equal("fr", read.Language);
        Assert.Equal("Dark", read.Theme);
        Assert.Equal("Ctrl", read.TriggerFirst);
        Assert.Equal("Shift", read.TriggerSecond);
        Assert.Equal("--verbose", read.ClaudeArgs);
        Assert.False(read.AutoFavicon);
        Assert.Equal(6, read.HotkeyModifiers);
        Assert.Equal(77, read.HotkeyKey);
    }

    /// <summary>La reprise écrit le fichier : elle ne doit avoir lieu qu'une fois.</summary>
    [Fact]
    public void Load_FichierAbsent_EcritLeFichier()
    {
        AppSettingsService.LoadFrom(File_, registry: name => name == "Language" ? "en" : null);

        Assert.True(System.IO.File.Exists(File_));

        // Deuxième lecture : le registre est vide, la valeur doit venir du fichier.
        Assert.Equal("en", AppSettingsService.LoadFrom(File_, registry: _ => null).Language);
    }

    [Fact]
    public void Load_FichierAbsentEtRegistreVide_RendLesDefauts()
    {
        var read = AppSettingsService.LoadFrom(File_, registry: _ => null);

        Assert.Equal("", read.Language);
        Assert.Equal("", read.Theme);
        Assert.True(read.AutoFavicon);
        Assert.Equal(0, read.HotkeyModifiers);
    }

    /// <summary>
    /// Une valeur de registre du mauvais type — écrite à la main, ou par une version differente —
    /// ne doit pas faire échouer la reprise de toutes les autres.
    /// </summary>
    [Fact]
    public void Load_ValeurDeRegistreDuMauvaisType_Ignoree()
    {
        object? Registry(string name) => name switch
        {
            "Language" => 42,          // devrait être une chaîne
            "HotkeyKey" => "quarante", // devrait être un entier
            "Theme" => "Light",
            _ => null,
        };

        var read = AppSettingsService.LoadFrom(File_, Registry);

        Assert.Equal("", read.Language);
        Assert.Equal(0, read.HotkeyKey);
        Assert.Equal("Light", read.Theme);
    }

    // ---------------------------------------------------------------- forme du fichier

    /// <summary>
    /// Le fichier est écrit en camelCase indenté, comme les autres configs du profil : il est fait
    /// pour être ouvert et modifié à la main.
    /// </summary>
    [Fact]
    public void Save_EcritUnJsonLisible()
    {
        AppSettingsService.SaveTo(File_, new AppSettings { Language = "fr" });

        var json = System.IO.File.ReadAllText(File_);

        Assert.Contains("\"language\": \"fr\"", json);
        Assert.Contains("\n", json);
        using var doc = JsonDocument.Parse(json);
        Assert.True(doc.RootElement.TryGetProperty("autoFavicon", out _));
    }
}
