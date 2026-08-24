using System.Windows.Input;
using DockPad.Services;

namespace DockPad.Tests;

/// <summary>
/// Overlay des raccourcis de tuiles : quelle touche désigne quelle case, et quels modificateurs
/// déclenchent quelle moitié.
/// </summary>
/// <remarks>
/// C'était le code le plus subtil de la fenêtre — remappage du pavé numérique, mode Auto des
/// modificateurs — et il n'avait aucun test : la seule façon de vérifier une modification était
/// d'ouvrir l'application et de presser des touches.
/// </remarks>
public class TileHintMapTests
{
    // --- Table touche → case

    [Theory]
    [InlineData(1, 0, 0)] [InlineData(2, 0, 1)] [InlineData(3, 0, 2)]
    [InlineData(4, 1, 0)] [InlineData(5, 1, 1)] [InlineData(6, 1, 2)]
    [InlineData(7, 2, 0)] [InlineData(8, 2, 1)] [InlineData(9, 2, 2)]
    public void MoitieGauche_LesNeufPremieresTouchesSeLisentCommeUnPave(int key, int row, int col)
    {
        // Lecture gauche → droite, haut → bas : 1 en haut à gauche, 9 en bas à droite du 3×3.
        Assert.Equal((row, col), TileHintMap.CellFor(key, firstHalf: true));
    }

    [Theory]
    [InlineData(1, 0, 3)] [InlineData(5, 1, 4)] [InlineData(9, 2, 5)]
    public void MoitieDroite_MemeTableDecaleeDeTroisColonnes(int key, int row, int col)
    {
        Assert.Equal((row, col), TileHintMap.CellFor(key, firstHalf: false));
    }

    [Theory]
    [InlineData(0, 3, 0)]    // 0 se place sous le 1
    [InlineData(10, 3, 1)]   // ↑
    [InlineData(11, 3, 2)]   // ↓
    public void DerniereLigne_ZeroPuisLesDeuxFleches(int key, int row, int col)
    {
        Assert.Equal((row, col), TileHintMap.CellFor(key, firstHalf: true));
    }

    // --- Touche → numéro

    [Theory]
    [InlineData(Key.D1, 1)]
    [InlineData(Key.D0, 0)]
    [InlineData(Key.NumPad7, 7)]
    public void Chiffres_DuHautOuDuPave(Key key, int expected)
    {
        Assert.Equal(expected, TileHintMap.KeyNumberFor(key, extended: false));
    }

    [Theory]
    [InlineData(Key.End, 1)] [InlineData(Key.Down, 2)] [InlineData(Key.Next, 3)]
    [InlineData(Key.Left, 4)] [InlineData(Key.Clear, 5)] [InlineData(Key.Right, 6)]
    [InlineData(Key.Home, 7)] [InlineData(Key.Up, 8)] [InlineData(Key.Prior, 9)]
    [InlineData(Key.Insert, 0)]
    public void PaveNumerique_NonEtendu_SeRemappeEnChiffres(Key key, int expected)
    {
        // Shift annule temporairement NumLock (comportement Windows) : les chiffres du pavé
        // arrivent alors en touches de navigation NON étendues. Sans ce remappage, Shift+1 du pavé
        // ne lançait rien.
        Assert.Equal(expected, TileHintMap.KeyNumberFor(key, extended: false));
    }

    [Theory]
    [InlineData(Key.Up, 10)]
    [InlineData(Key.Down, 11)]
    public void VraiesFleches_Etendues_GardentLeurRoleDeDerniereLigne(Key key, int expected)
    {
        // Les vraies flèches sont étendues : elles désignent les deux dernières cases, elles ne
        // valent pas 8 et 2.
        Assert.Equal(expected, TileHintMap.KeyNumberFor(key, extended: true));
    }

    [Theory]
    [InlineData(Key.A)]
    [InlineData(Key.F1)]
    [InlineData(Key.Escape)]
    public void ToucheSansRole_RendNull(Key key)
    {
        Assert.Null(TileHintMap.KeyNumberFor(key, extended: false));
    }

    // --- Modificateurs

    [Fact]
    public void Configuration_Explicite_EstRespectee()
    {
        var (first, second) = TileHintMap.ResolveTriggers("Alt", "Shift", hotkeyModifiers: 0);

        Assert.Equal(ModifierKeys.Alt, first);
        Assert.Equal(ModifierKeys.Shift, second);
    }

    [Fact]
    public void Configuration_DeuxFoisLeMeme_RetombeEnAuto()
    {
        // Deux moitiés sur le même modificateur, ce serait une moitié inatteignable.
        var (first, second) = TileHintMap.ResolveTriggers("Ctrl", "Ctrl", hotkeyModifiers: 0);

        Assert.NotEqual(first, second);
    }

    [Fact]
    public void Configuration_APeineAMoitie_RetombeEnAuto()
    {
        var (first, second) = TileHintMap.ResolveTriggers("Ctrl", "", hotkeyModifiers: 0);

        Assert.Equal(ModifierKeys.Control, first);
        Assert.Equal(ModifierKeys.Shift, second);
    }

    [Fact]
    public void Auto_RaccourciGlobalAvecCtrl_EvitteCtrl()
    {
        // Sinon le raccourci global et l'overlay se disputeraient la même touche.
        var (first, second) = TileHintMap.ResolveTriggers("", "", HotkeyService.MOD_CONTROL);

        Assert.Equal(ModifierKeys.Shift, first);
        Assert.Equal(ModifierKeys.Alt, second);
        Assert.DoesNotContain(ModifierKeys.Control, new[] { first, second });
    }

    [Fact]
    public void Auto_RaccourciGlobalSansCtrl_UtiliseCtrlEtShift()
    {
        var (first, second) = TileHintMap.ResolveTriggers("", "", HotkeyService.MOD_ALT);

        Assert.Equal(ModifierKeys.Control, first);
        Assert.Equal(ModifierKeys.Shift, second);
    }
}
