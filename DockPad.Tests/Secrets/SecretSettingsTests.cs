using System.IO;
using DockPad.Models;
using DockPad.Services;

namespace DockPad.Tests.Secrets;

/// <summary>
/// Les trois réglages de l'injection de secrets dans <c>settings.json</c>.
/// </summary>
/// <remarks>
/// Ils sont des <b>préférences</b>, pas de la matière secrète — un chemin, un nombre, un nom
/// d'organisation. C'est pourquoi ils vivent hors du dossier <c>Secrets/</c> comme les autres
/// options, alors que la frontière d'audit est par ailleurs stricte.
/// </remarks>
public class SecretSettingsTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "dockpad-inject-" + Guid.NewGuid().ToString("N"));

    public SecretSettingsTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch (IOException) { }
        GC.SuppressFinalize(this);
    }

    private string File_ => Path.Combine(_dir, "settings.json");

    [Fact]
    public void UnFichierEcritParUneVersionAnterieure_GardeLesDefauts()
    {
        // Le piège que le projet a déjà rencontré avec autoFavicon : une clé absente doit donner le
        // comportement attendu, et non zéro. System.Text.Json laisse l'initialiseur en place.
        System.IO.File.WriteAllText(File_, """{"language":"fr","theme":"Dark"}""");

        var read = AppSettingsService.LoadFrom(File_, registry: _ => null);

        Assert.Equal("", read.BitwardenCliPath);
        Assert.Equal(90, read.ClipboardClearSeconds);
        Assert.Equal("", read.VaultOrganization);
    }

    [Fact]
    public void QuatreVingtDixSecondesEtNonTrente()
    {
        // La cible de collage est une interface web dans un navigateur : il faut le temps de
        // trouver l'onglet et d'ouvrir la bonne page.
        Assert.Equal(90, new AppSettings().ClipboardClearSeconds);
    }

    [Fact]
    public void SavePuisLoad_ConserveLesTrois()
    {
        AppSettingsService.SaveTo(File_, new AppSettings
        {
            BitwardenCliPath = @"C:\bw\bw.exe",
            ClipboardClearSeconds = 0,
            VaultOrganization = "NAS QNAP",
        });

        var read = AppSettingsService.LoadFrom(File_, registry: _ => null);

        Assert.Equal(@"C:\bw\bw.exe", read.BitwardenCliPath);
        Assert.Equal(0, read.ClipboardClearSeconds);
        Assert.Equal("NAS QNAP", read.VaultOrganization);
    }

    [Fact]
    public void LaRepriseDuRegistreNeLesCherchePas()
    {
        // Ces réglages n'ont jamais vécu dans le registre : aller y chercher leurs clés
        // inventerait une migration qui n'a jamais eu lieu.
        var lus = new List<string>();

        AppSettingsService.FromRegistry(name => { lus.Add(name); return null; });

        Assert.DoesNotContain("BitwardenCliPath", lus);
        Assert.DoesNotContain("ClipboardClearSeconds", lus);
        Assert.DoesNotContain("VaultOrganization", lus);
    }
}
