using System.Reflection;
using DockPad.Services;

namespace DockPad.Tests;

/// <summary>
/// Le libellé de version affiché dans les pieds de fenêtre.
/// </summary>
/// <remarks>
/// Ce type part dans une bibliothèque partagée par deux applications : chacune doit afficher
/// <b>sa</b> version, pas celle de la bibliothèque.
/// </remarks>
public class AppInfoTests
{
    [Fact]
    public void FormateLaVersionDeLAssemblyQuOnLuiDonne()
    {
        // On teste la fonction PURE, pas le choix de l'assembly : sous un hôte de test,
        // GetEntryAssembly() peut être nul ou être celui du lanceur, et un test qui en dépend
        // échouerait pour une raison sans rapport avec ce qu'il prétend vérifier.
        var assembly = typeof(AppInfo).Assembly;
        var v = assembly.GetName().Version!;

        Assert.Equal($"v{v.Major}.{v.Minor}.{v.Build}", AppInfo.Text(assembly));
    }

    [Fact]
    public void UneAssemblySansVersion_NeRendRien()
    {
        Assert.Equal("", AppInfo.Text(null));
    }

    [Fact]
    public void LAssemblyPeutEtrePosee_PourUnHoteQuiMontreLesFenetresDUnAutre()
    {
        // Un outil de capture EST l'assembly d'entrée, mais il affiche les fenêtres de DockPad :
        // sans cette pose explicite, les captures de documentation portaient la version de l'outil
        // (v1.0.0) au lieu de celle du produit. Vérifié à l'écran, pas déduit.
        var assembly = typeof(AppInfo).Assembly;
        var v = assembly.GetName().Version!;

        AppInfo.Initialize(assembly);

        Assert.Equal($"v{v.Major}.{v.Minor}.{v.Build}", AppInfo.VersionText);
    }
}
