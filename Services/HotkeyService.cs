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

    public static readonly (string Name, uint VK)[] Keys =
    [
        ("Espace",   0x20),
        ("Tab",      0x09),
        ("Entrée",   0x0D),
        ("Échap",    0x1B),
        ("Suppr",    0x2E),
        ("Inser",    0x2D),
        ("Début",    0x24),
        ("Fin",      0x23),
        ("PgPréc",   0x21),
        ("PgSuiv",   0x22),
        ("↑",        0x26),
        ("↓",        0x28),
        ("←",        0x25),
        ("→",        0x27),
        .. Enumerable.Range(0, 10).Select(i => ($"Num{i}", (uint)(0x60 + i))),
        .. Enumerable.Range(0, 26).Select(i => (((char)('A' + i)).ToString(), (uint)('A' + i))),
        .. Enumerable.Range(1, 12).Select(i => ($"F{i}", (uint)(0x6F + i))),
    ];

    public static string KeyName(uint vk)
    {
        foreach (var (name, code) in Keys)
            if (code == vk) return name;
        return $"0x{vk:X2}";
    }

    [DllImport("user32.dll")]
    public static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

    [DllImport("user32.dll")]
    public static extern bool UnregisterHotKey(IntPtr hWnd, int id);
}
