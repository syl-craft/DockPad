namespace DockPad.Models;

/// <summary>
/// Laquelle des deux grilles de tuiles : les raccourcis, ou les favoris.
/// </summary>
/// <remarks>
/// <para>
/// Les deux grilles ont exactement le même modèle — une <c>List&lt;ShortcutEntry&gt;</c> et une
/// <c>List&lt;PageConfig&gt;</c> — et exactement le même comportement. Elles ne diffèrent que par le
/// fichier qui les porte, et c'est <see cref="Services.TileStore"/> qui le sait.
/// </para>
/// <para>
/// <b>Le défaut est toujours <see cref="Shortcuts"/></b>, partout où cette cible est un paramètre
/// optionnel : c'est ce qui a permis d'ajouter les favoris sans toucher aucun des appelants
/// existants, dans la fenêtre comme dans le serveur MCP.
/// </para>
/// </remarks>
public enum TileTarget
{
    Shortcuts,
    Favorites,
}
