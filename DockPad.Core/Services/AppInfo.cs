using System.Reflection;

namespace DockPad.Services;

/// <summary>Informations sur l'application, affichées dans les footers des fenêtres de config.</summary>
public static class AppInfo
{
    private static Assembly? _assembly;

    /// <summary>
    /// Pose l'assembly dont la version est affichée. Sans appel, c'est celle d'entrée.
    /// </summary>
    /// <remarks>
    /// Pour les <b>outils de capture</b> : ils sont l'assembly d'entrée, mais montrent les fenêtres
    /// de l'application. Sans cette pose, les captures de documentation portaient la version de
    /// l'outil — <c>v1.0.0</c> — au lieu de celle du produit. Constaté à l'écran en extrayant la
    /// bibliothèque, pas déduit.
    /// </remarks>
    public static void Initialize(Assembly? assembly) => _assembly = assembly;

    /// <summary>Version semver affichée (ex. « v1.14.0 »).</summary>
    /// <remarks>
    /// L'assembly d'<b>entrée</b>, jamais l'exécutante : ce type vit dans une bibliothèque partagée
    /// par plusieurs applications, et chacune doit afficher <b>sa</b> version, pas celle de la
    /// bibliothèque. Le défaut ne se verrait que dans un pied de fenêtre, donc tard, et ne se
    /// relierait à rien.
    /// </remarks>
    public static string VersionText =>
        Text(_assembly ?? Assembly.GetEntryAssembly() ?? Assembly.GetExecutingAssembly());

    /// <summary>Décision pure : le libellé d'une assembly donnée.</summary>
    public static string Text(Assembly? assembly)
    {
        var v = assembly?.GetName().Version;
        return v != null ? $"v{v.Major}.{v.Minor}.{v.Build}" : "";
    }
}
