using System.Reflection;

namespace DockPad.Services;

/// <summary>Informations sur l'application, affichées dans les footers des fenêtres de config.</summary>
public static class AppInfo
{
    /// <summary>Version semver affichée (ex. « v1.8.0 »).</summary>
    public static string VersionText
    {
        get
        {
            var v = Assembly.GetExecutingAssembly().GetName().Version;
            return v != null ? $"v{v.Major}.{v.Minor}.{v.Build}" : "";
        }
    }
}
