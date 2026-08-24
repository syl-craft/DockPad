using System.Windows.Input;

namespace DockPad.Services;

/// <summary>
/// Règles de l'overlay des raccourcis de tuiles : quelle touche désigne quelle case, et quels
/// modificateurs déclenchent quelle moitié de la grille.
/// </summary>
/// <remarks>
/// <para>
/// Sorti du code-behind parce que c'est du <b>calcul</b>, pas de l'affichage : le rendu de l'overlay
/// reste dans la fenêtre, la table et les règles vivent ici et se testent. C'était la partie la plus
/// subtile du fichier — remappage du pavé numérique, mode Auto — et la seule façon de vérifier une
/// modification était d'ouvrir l'application et de presser des touches.
/// </para>
/// <para>
/// <see cref="KeyNumberFor"/> prend le drapeau « touche étendue » en <b>paramètre</b> plutôt que de
/// le lire lui-même : sa source est le message clavier en cours, un état global de WPF que la vue
/// est seule à pouvoir consulter au bon moment.
/// </para>
/// </remarks>
public static class TileHintMap
{
    /// <summary>Case désignée par un numéro de touche, dans la moitié gauche ou droite.</summary>
    /// <remarks>
    /// Les neuf premières touches se lisent comme un pavé : gauche → droite, haut → bas. La dernière
    /// ligne porte le 0 sous le 1, puis les deux flèches.
    /// </remarks>
    public static (int Row, int Col) CellFor(int keyNumber, bool firstHalf)
    {
        int baseCol = firstHalf ? 0 : 3;

        return keyNumber switch
        {
            0 => (3, baseCol),
            10 => (3, baseCol + 1),   // ↑
            11 => (3, baseCol + 2),   // ↓
            _ => ((keyNumber - 1) / 3, (keyNumber - 1) % 3 + baseCol),
        };
    }

    /// <summary>
    /// Numéro de touche pour l'overlay, ou <c>null</c> si la touche n'a pas de rôle.
    /// </summary>
    /// <param name="extended">
    /// Drapeau « touche étendue » du message clavier en cours (bit 24 du <c>lParam</c>). Les vraies
    /// flèches sont étendues ; les mêmes codes émis par le pavé numérique ne le sont pas.
    /// </param>
    public static int? KeyNumberFor(Key key, bool extended)
    {
        // Shift annule temporairement NumLock — comportement Windows — et les chiffres du pavé
        // arrivent alors en touches de navigation non étendues. Sans ce remappage, Shift + 1 du pavé
        // ne lance rien. WPF n'expose pas ce drapeau, d'où le paramètre.
        if (!extended)
        {
            int? numpad = key switch
            {
                Key.Insert => 0, Key.End => 1, Key.Down => 2, Key.Next => 3,
                Key.Left => 4, Key.Clear => 5, Key.Right => 6, Key.Home => 7,
                Key.Up => 8, Key.Prior => 9,
                _ => null,
            };
            if (numpad is not null) return numpad;
        }

        if (key == Key.Up) return 10;
        if (key == Key.Down) return 11;

        // VK_0..VK_9 et VK_NUMPAD0..VK_NUMPAD9
        int vk = KeyInterop.VirtualKeyFromKey(key);
        if (vk is >= 0x30 and <= 0x39) return vk - 0x30;
        if (vk is >= 0x60 and <= 0x69) return vk - 0x60;

        return null;
    }

    /// <summary>
    /// Modificateurs des deux moitiés, d'après la configuration et le raccourci global.
    /// </summary>
    /// <remarks>
    /// Une configuration explicite l'emporte, à condition que les deux moitiés diffèrent — sinon
    /// l'une d'elles serait inatteignable. À défaut, le mode Auto évite le modificateur du raccourci
    /// global, faute de quoi les deux se disputeraient la même touche.
    /// </remarks>
    public static (ModifierKeys First, ModifierKeys Second) ResolveTriggers(
        string configuredFirst, string configuredSecond, uint hotkeyModifiers)
    {
        var first = Parse(configuredFirst);
        var second = Parse(configuredSecond);

        if (first is not null && second is not null && first != second)
            return (first.Value, second.Value);

        return (hotkeyModifiers & HotkeyService.MOD_CONTROL) != 0
            ? (ModifierKeys.Shift, ModifierKeys.Alt)
            : (ModifierKeys.Control, ModifierKeys.Shift);
    }

    /// <summary>Nom stocké dans le registre → modificateur, ou <c>null</c> pour « automatique ».</summary>
    public static ModifierKeys? Parse(string name) => name switch
    {
        "Ctrl" => ModifierKeys.Control,
        "Alt" => ModifierKeys.Alt,
        "Shift" => ModifierKeys.Shift,
        _ => null,
    };
}
