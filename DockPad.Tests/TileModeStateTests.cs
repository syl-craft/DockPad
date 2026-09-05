using DockPad.Models;
using DockPad.Services;
using DockPad.Services.Localization;

namespace DockPad.Tests;

/// <summary>
/// Le mode affiché par la fenêtre : les raccourcis, ou les favoris.
/// </summary>
/// <remarks>
/// L'état vit dans un service et non dans le code-behind, pour la même raison que
/// <see cref="TileLockState"/> juste à côté : le glyphe, l'infobulle et la cible sont des décisions,
/// elles se testent sans WPF.
/// </remarks>
public class TileModeStateTests
{
    [Fact]
    public void ParDefaut_CeSontLesRaccourcis()
    {
        // Le mode d'usage courant. C'est aussi le défaut de la cible partout ailleurs : les
        // appelants existants ne changent pas de sens.
        var state = new TileModeState();

        Assert.False(state.IsFavorites);
        Assert.Equal(TileTarget.Shortcuts, state.Target);
        Assert.Equal(Loc.T("Quick_TileMode_ToFavorites"), state.Tooltip);
    }

    [Fact]
    public void Toggle_PasseAuxFavoris()
    {
        var state = new TileModeState();

        state.Toggle();

        Assert.True(state.IsFavorites);
        Assert.Equal(TileTarget.Favorites, state.Target);
        Assert.Equal(Loc.T("Quick_TileMode_ToShortcuts"), state.Tooltip);
    }

    [Fact]
    public void Toggle_DeuxFois_RevientAuxRaccourcis()
    {
        var state = new TileModeState();

        state.Toggle();
        state.Toggle();

        Assert.Equal(TileTarget.Shortcuts, state.Target);
    }

    [Fact]
    public void LesDeuxGlyphes_Different()
    {
        // MinWidth="40" sur le bouton compense leur largeur inégale, mais encore faut-il qu'ils
        // ne soient pas les mêmes : sinon rien à l'écran ne dit dans quel mode on est.
        var state = new TileModeState();
        var shortcuts = state.Glyph;

        state.Toggle();

        Assert.NotEqual(shortcuts, state.Glyph);
    }

    [Fact]
    public void Reset_DepuisLesFavoris_RevientAuxRaccourcis()
    {
        // C'est ce que fait le masquage ou la réduction de la fenêtre, branché sur le même point
        // unique que le verrou des tuiles : le mode est un détour, pas un état qu'on oublie ouvert.
        var state = new TileModeState();
        state.Toggle();

        state.Reset();

        Assert.False(state.IsFavorites);
    }

    [Fact]
    public void Reset_DejaSurLesRaccourcis_NeChangeRien()
    {
        // Le cycle de vie de la fenêtre passe ici plusieurs fois.
        var state = new TileModeState();

        state.Reset();

        Assert.False(state.IsFavorites);
    }
}
