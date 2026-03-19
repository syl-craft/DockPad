using System.Runtime.InteropServices;
using System.Text;

namespace DockPad.Services;

public static class ResourceStringResolver
{
    [DllImport("shlwapi.dll", CharSet = CharSet.Unicode)]
    private static extern int SHLoadIndirectString(string pszSource, StringBuilder pszOutBuf, int cchOutBuf, IntPtr ppvReserved);

    /// <summary>
    /// Resolves a Windows indirect resource string like "@shell32.dll,-8506"
    /// into its localized display name. Returns the original string if not resolvable.
    /// </summary>
    public static string Resolve(string value)
    {
        if (string.IsNullOrWhiteSpace(value)) return value;
        if (!value.TrimStart().StartsWith('@')) return value;

        try
        {
            var sb = new StringBuilder(512);
            int hr = SHLoadIndirectString(value, sb, sb.Capacity, IntPtr.Zero);
            if (hr == 0 && sb.Length > 0)
                return sb.ToString().Replace("&", "");
        }
        catch { }

        return value;
    }
}
