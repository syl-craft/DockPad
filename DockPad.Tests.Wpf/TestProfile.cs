using System.IO;
using System.Runtime.CompilerServices;
using DockPad.Services;

namespace DockPad.Tests;

/// <summary>
/// Profil temporaire de tout le processus de test : la fenêtre construite ici ne lit ni n'écrit jamais le profil de
/// l'utilisateur, et son résultat ne dépend pas des tuiles de la machine.
/// </summary>
internal static class TestProfile
{
    public static string Root { get; } = Path.Combine(Path.GetTempPath(), $"dockpad-tests-wpf-{Guid.NewGuid():N}");

    /// <summary>
    /// Avant toute résolution de <see cref="AppPaths.ProfileRoot"/>, qui ne se lit qu'une fois.
    /// </summary>
    [ModuleInitializer]
    internal static void Install()
    {
        Directory.CreateDirectory(Root);
        Environment.SetEnvironmentVariable(AppPaths.OverrideVariable, Root);
        AppDomain.CurrentDomain.ProcessExit += (_, _) =>
        {
            try { Directory.Delete(Root, recursive: true); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        };
    }
}
