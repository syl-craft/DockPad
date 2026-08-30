using Microsoft.Win32;

namespace DockPad.Secrets;

/// <summary>
/// L'entrée « Injecter les secrets… » du menu contextuel de l'Explorateur.
/// </summary>
/// <remarks>
/// <para>
/// <b>Une seule clé, sur tous les fichiers, sous <c>HKCU</c></b> — aucun droit administrateur, et
/// une désinstallation qui ne laisse rien. C'est le motif per-user qu'emploient déjà VS Code et
/// MobaXterm sur un poste ordinaire.
/// </para>
/// <para>
/// <b>Pourquoi pas un ciblage par extension.</b> La première version posait une clé par extension
/// sous <c>SystemFileAssociations</c>. Aucune liste ne pouvait suffire : les fichiers <b>sans
/// extension</b> (Dockerfile, Makefile) n'y sont pas atteignables du tout, et Windows lit
/// l'extension de <c>.env.prod</c> comme <c>.prod</c> — le cas le plus courant échappait à la liste
/// qui prétendait le couvrir. Le prix payé est une ligne de plus dans le menu de chaque fichier,
/// sous « Afficher plus d'options » ; un clic sur un binaire échoue proprement, faute de marqueur.
/// </para>
/// <para>
/// <b>Sur Windows 11, l'entrée est sous « Afficher plus d'options »</b>, ou directement avec
/// Maj + clic droit : le menu principal n'accepte que des extensions packagées et signées, ce qui
/// serait disproportionné ici. Le texte d'aide des Options le dit — sans lui, on installe l'entrée,
/// on ne la voit pas, et on conclut que ça ne marche pas.
/// </para>
/// </remarks>
public static class SecretMenu
{
    /// <summary>Nom de la clé, stable et jamais traduit — c'est un identifiant.</summary>
    private const string KeyName = "DockPadInjectSecrets";

    /// <summary>L'unique clé : <c>*</c> désigne tous les fichiers, extension ou non.</summary>
    public static string KeyPath => $@"Software\Classes\*\shell\{KeyName}";

    // ───────────── Décisions pures ─────────────

    public static string BuildCommand(string exePath) => $"\"{exePath}\" --inject-secrets \"%1\"";

    /// <summary>
    /// L'entrée est-elle installée <b>et</b> pointe-t-elle sur cet exécutable ?
    /// </summary>
    /// <remarks>
    /// Le chemin entre dans la comparaison, comme pour l'état d'enregistrement navigateur : un
    /// DockPad déplacé laisse derrière lui une entrée qui ne lance plus rien, et l'annoncer
    /// « installée » ferait chercher le problème ailleurs.
    /// </remarks>
    /// <param name="readCommand">Lit la commande d'une clé, <c>null</c> si la clé est absente.</param>
    public static bool IsInstalledIn(Func<string, string?> readCommand, string exePath) =>
        readCommand(KeyPath) == BuildCommand(exePath);

    // ───────────── Registre réel ─────────────

    public static bool IsInstalled() => IsInstalledIn(ReadCommand, ExePath);

    /// <summary>
    /// Pose la clé. Idempotent, et volontairement réécrit à chaque fois : c'est ce qui fait suivre
    /// le libellé traduit après un changement de langue.
    /// </summary>
    public static void Install()
    {
        var exe = ExePath;

        using var key = Registry.CurrentUser.CreateSubKey(KeyPath, writable: true)
            ?? throw new InvalidOperationException($"Cannot create {KeyPath}");

        key.SetValue(null, Loc.T("Inject_ContextMenu_Label"));
        key.SetValue("Icon", $"{exe},0");

        using var command = key.CreateSubKey("command", writable: true)
            ?? throw new InvalidOperationException("Cannot create the command subkey");
        command.SetValue(null, BuildCommand(exe));
    }

    /// <summary>Retire la clé. Ne laisse rien derrière.</summary>
    public static void Uninstall() =>
        Registry.CurrentUser.DeleteSubKeyTree(KeyPath, throwOnMissingSubKey: false);

    /// <summary>Le chemin réel de l'exécutable en cours — celui que l'entrée doit lancer.</summary>
    private static string ExePath =>
        Environment.ProcessPath
        ?? System.Diagnostics.Process.GetCurrentProcess().MainModule!.FileName;

    private static string? ReadCommand(string keyPath)
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey($@"{keyPath}\command");
            return key?.GetValue(null)?.ToString();
        }
        catch (Exception ex)
        {
            Services.LogService.Warn(ex, $"Lecture de HKCU\\{keyPath}");
            return null;
        }
    }
}
