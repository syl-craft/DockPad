using System.IO;
using System.Globalization;

namespace DockPad.Services;

/// <summary>
/// Copie horodatée des fichiers de configuration dans <c>.backup\</c>.
/// </summary>
/// <remarks>
/// C'est le filet de l'utilisateur avant une manipulation risquée : il mérite d'être testé. La
/// logique vivait dans un gestionnaire de clic, mêlée au dialogue de confirmation, donc
/// invérifiable — le seul moyen de savoir si elle marchait était de cliquer.
/// </remarks>
public static class ConfigBackup
{
    /// <summary>Dossier de sauvegarde du profil.</summary>
    public static string DirectoryFor(string profileRoot) => Path.Combine(profileRoot, ".backup");

    /// <summary>
    /// Copie les fichiers existants dans <c>.backup\</c>, chacun suffixé de l'horodatage, et rend le
    /// dossier de destination.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Un fichier absent est <b>ignoré</b> et n'interrompt pas les autres : une configuration jamais
    /// ouverte n'a pas de fichier, ce n'est pas une erreur.
    /// </para>
    /// <para>
    /// Le suffixe est à la seconde, donc deux sauvegardes rapprochées se disputaient le même nom :
    /// <c>File.Copy</c> levait « le fichier existe déjà » au milieu de la boucle, laissant certaines
    /// configurations sauvegardées et d'autres non. Un indice est ajouté en cas de collision.
    /// </para>
    /// </remarks>
    public static string Run(string profileRoot, IEnumerable<string> files, DateTime now)
    {
        var backupDir = DirectoryFor(profileRoot);
        Directory.CreateDirectory(backupDir);

        var stamp = now.ToString("yyyyMMdd_HHmmss", CultureInfo.InvariantCulture);

        foreach (var source in files)
        {
            if (!File.Exists(source)) continue;

            var name = Path.GetFileNameWithoutExtension(source);
            var ext = Path.GetExtension(source);

            var dest = Path.Combine(backupDir, $"{name}_{stamp}{ext}");
            for (var i = 2; File.Exists(dest); i++)
                dest = Path.Combine(backupDir, $"{name}_{stamp}_{i}{ext}");

            File.Copy(source, dest);
        }

        return backupDir;
    }

    /// <summary>Les cinq configurations du profil, dans l'ordre où elles apparaissent à l'écran.</summary>
    public static string[] ProfileFiles() =>
    [
        AppSettingsService.FilePath,
        ShortcutService.FilePath,
        PageConfigService.FilePath,
        BrowserConfigService.FilePath,
        McpConfigService.FilePath,
        UsageConfigService.FilePath,
    ];
}
