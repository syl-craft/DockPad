using System.IO;

namespace DockPad.Services;

/// <summary>
/// Lecture de ce qu'on glisse depuis l'Explorateur : un dossier ou un raccourci Internet.
/// </summary>
/// <remarks>
/// Sortie du code-behind parce qu'un <c>.url</c> est un petit format de fichier à part entière —
/// sections, clés, casse libre, lignes inattendues — et que le seul moyen d'en vérifier un cas
/// limite était jusqu'ici de fabriquer le fichier et de le glisser sur la fenêtre.
/// </remarks>
public static class DroppedShortcut
{
    /// <summary>Contenu utile d'un raccourci Internet.</summary>
    public sealed record UrlShortcut(string Url, string Name);

    /// <summary>
    /// Le chemin déposé donne-t-il quelque chose dont on sait faire une tuile ? Un dossier ou un
    /// <c>.url</c> — pas un fichier quelconque, qui n'aurait pas de type de tuile évident.
    /// </summary>
    public static bool IsAcceptable(string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return false;

        return Directory.Exists(path)
            || Path.GetExtension(path).Equals(".url", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Nom de tuile pour un dossier déposé.</summary>
    /// <remarks>
    /// <c>Path.GetFileName</c> rend une chaîne vide sur une racine de lecteur (« C:\ ») et sur un
    /// chemin terminé par un séparateur : d'où le repli, sans quoi la tuile s'appellerait « ».
    /// </remarks>
    public static string FolderName(string folderPath)
    {
        // Couper le séparateur AVANT : « C:\dev\projets\ » donnait sinon le chemin entier comme nom
        // de tuile, puisque GetFileName rend vide et que le repli prenait tout.
        var trimmed = folderPath.TrimEnd('\\', '/');
        var name = Path.GetFileName(trimmed);

        return name.Length > 0 ? name : trimmed;   // racine de lecteur : « C: »
    }

    /// <summary>
    /// URL et titre d'un fichier <c>.url</c>, ou <c>null</c> s'il n'y a pas d'URL — un raccourci sans
    /// adresse ne peut pas faire une tuile, et mieux vaut ne rien créer qu'une tuile morte.
    /// </summary>
    /// <remarks>
    /// Le titre est facultatif : les <c>.url</c> exportés par Chrome et Edge n'en portent pas, on
    /// retombe alors sur le nom du fichier. Les clés sont comparées sans tenir compte de la casse,
    /// le format ne la fixe pas.
    /// </remarks>
    public static UrlShortcut? FromUrlFile(string path)
    {
        string? url = null;
        string? title = null;

        try
        {
            foreach (var line in File.ReadLines(path))
            {
                if (line.StartsWith("URL=", StringComparison.OrdinalIgnoreCase)) url = line[4..];
                else if (line.StartsWith("Title=", StringComparison.OrdinalIgnoreCase)) title = line[6..];
            }
        }
        catch (Exception ex)
        {
            LogService.Warn(ex, $"Lecture du fichier .url déposé : {path}");
            return null;
        }

        if (string.IsNullOrEmpty(url)) return null;

        return new UrlShortcut(url, title ?? Path.GetFileNameWithoutExtension(path));
    }
}
