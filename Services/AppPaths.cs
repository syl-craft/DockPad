using System.IO;

namespace DockPad.Services;

/// <summary>
/// Emplacement du profil DockPad (configs JSON, store d'icônes, logs) :
/// %APPDATA%\DockPad par défaut, ou le dossier indiqué par la variable
/// d'environnement DOCKPAD_PROFILE_DIR. Sert aux profils portables et aux outils
/// de capture, qui pointent un dossier de fixture au lieu du profil de l'utilisateur.
/// Résolu une fois au démarrage : changer la variable ensuite n'a aucun effet.
/// </summary>
public static class AppPaths
{
    public const string OverrideVariable = "DOCKPAD_PROFILE_DIR";

    public static readonly string ProfileRoot = Resolve(
        Environment.GetEnvironmentVariable(OverrideVariable),
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData));

    /// <summary>
    /// Dossier de profil : la surcharge est prise telle quelle (aucun sous-dossier
    /// « DockPad » ajouté, pour que le dossier indiqué soit bien celui utilisé),
    /// sinon &lt;appData&gt;\DockPad.
    /// </summary>
    public static string Resolve(string? overrideDir, string appData)
    {
        var dir = overrideDir?.Trim().Trim('"').Trim();
        return string.IsNullOrEmpty(dir)
            ? Path.Combine(appData, "DockPad")
            : Path.GetFullPath(dir);
    }

    /// <summary>Chemin d'un fichier du profil (ex. AppPaths.File("browsers.json")).</summary>
    public static string File(string name) => Path.Combine(ProfileRoot, name);
}
