using System.Runtime.InteropServices;

namespace DockPad.Services;

public static class HotkeyService
{
    public const uint MOD_ALT      = 0x0001;
    public const uint MOD_CONTROL  = 0x0002;
    public const uint MOD_SHIFT    = 0x0004;
    public const uint MOD_WIN      = 0x0008;
    public const uint MOD_NOREPEAT = 0x4000;

    public const int WM_HOTKEY = 0x0312;
    public const int HotkeyId  = 9001;

    /// <summary>
    /// Touches proposées pour le raccourci global. <c>Name</c> est un <b>identifiant stable</b>, pas
    /// un libellé : les dix premières se traduisent, via <see cref="Display"/>. Ce qui est stocké
    /// dans le registre est le code virtuel, jamais le nom — changer de langue ne touche donc à aucun
    /// raccourci enregistré.
    /// </summary>
    public static readonly (string Name, uint VK)[] Keys =
    [
        ("Space",    0x20),
        ("Tab",      0x09),
        ("Enter",    0x0D),
        ("Esc",      0x1B),
        ("Delete",   0x2E),
        ("Insert",   0x2D),
        ("Home",     0x24),
        ("End",      0x23),
        ("PageUp",   0x21),
        ("PageDown", 0x22),
        ("↑",        0x26),
        ("↓",        0x28),
        ("←",        0x25),
        ("→",        0x27),
        .. Enumerable.Range(0, 10).Select(i => ($"Num{i}", (uint)(0x60 + i))),
        .. Enumerable.Range(0, 26).Select(i => (((char)('A' + i)).ToString(), (uint)('A' + i))),
        .. Enumerable.Range(1, 12).Select(i => ($"F{i}", (uint)(0x6F + i))),
    ];

    /// <summary>
    /// Libellé affichable d'une touche. Seules les touches nommées ont une traduction ; les lettres,
    /// les F1-F12, le pavé numérique et les flèches s'écrivent pareil partout et n'entrent pas dans
    /// le magasin de chaînes — y mettre « A » serait du bruit.
    /// </summary>
    public static string Display(string name) => name switch
    {
        "Space" or "Tab" or "Enter" or "Esc" or "Delete" or "Insert"
            or "Home" or "End" or "PageUp" or "PageDown" => Loc.T($"Key_{name}"),
        _ => name,
    };

    public static string KeyName(uint vk)
    {
        foreach (var (name, code) in Keys)
            if (code == vk) return Display(name);
        return $"0x{vk:X2}";
    }

    [DllImport("user32.dll")]
    public static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

    [DllImport("user32.dll")]
    public static extern bool UnregisterHotKey(IntPtr hWnd, int id);
}
