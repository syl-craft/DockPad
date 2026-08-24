using System.IO;
using DockPad.Services;

namespace DockPad.Tests;

/// <summary>
/// Sauvegarde des fichiers de configuration.
/// </summary>
/// <remarks>
/// C'est le filet de l'utilisateur avant une manipulation risquée : il mérite un test. La logique
/// vivait dans un gestionnaire de clic, mêlée au dialogue de confirmation, donc invérifiable.
/// </remarks>
public class ConfigBackupTests : IDisposable
{
    private readonly string _profile = Path.Combine(Path.GetTempPath(), $"backup_{Guid.NewGuid():N}");

    public ConfigBackupTests() => Directory.CreateDirectory(_profile);

    public void Dispose()
    {
        if (Directory.Exists(_profile)) Directory.Delete(_profile, recursive: true);
    }

    private string Write(string name, string content = "{}")
    {
        var path = Path.Combine(_profile, name);
        File.WriteAllText(path, content);
        return path;
    }

    private static DateTime Moment => new(2026, 8, 24, 21, 5, 3);

    [Fact]
    public void CopieChaqueFichierAvecUnHorodatage()
    {
        Write("shortcuts.json", "[1]");
        Write("pages.json", "[2]");

        var dir = ConfigBackup.Run(_profile,
            [Path.Combine(_profile, "shortcuts.json"), Path.Combine(_profile, "pages.json")], Moment);

        Assert.Equal("[1]", File.ReadAllText(Path.Combine(dir, "shortcuts_20260824_210503.json")));
        Assert.Equal("[2]", File.ReadAllText(Path.Combine(dir, "pages_20260824_210503.json")));
    }

    [Fact]
    public void FichierAbsent_EstIgnoreSansEchouer()
    {
        // Une configuration jamais ouverte n'a pas de fichier : ce n'est pas une erreur, et la
        // sauvegarde des autres ne doit pas s'arrêter là.
        Write("shortcuts.json");

        var dir = ConfigBackup.Run(_profile,
            [Path.Combine(_profile, "shortcuts.json"), Path.Combine(_profile, "jamais-ecrit.json")],
            Moment);

        Assert.Single(Directory.GetFiles(dir));
    }

    [Fact]
    public void DeuxSauvegardesDansLaMemeSeconde_NEcrasentPasLaPremiere()
    {
        // L'horodatage est à la seconde : deux clics rapprochés tombaient sur le même nom et
        // File.Copy levait « le fichier existe déjà », en plein milieu de la boucle — certaines
        // configurations sauvegardées, d'autres non.
        Write("shortcuts.json");
        var files = new[] { Path.Combine(_profile, "shortcuts.json") };

        var dir = ConfigBackup.Run(_profile, files, Moment);
        ConfigBackup.Run(_profile, files, Moment);

        Assert.Equal(2, Directory.GetFiles(dir).Length);
    }

    [Fact]
    public void CreeLeDossierDeSauvegardeSIlNExistePas()
    {
        Write("shortcuts.json");

        var dir = ConfigBackup.Run(_profile, [Path.Combine(_profile, "shortcuts.json")], Moment);

        Assert.True(Directory.Exists(dir));
        Assert.EndsWith(".backup", dir);
    }
}
