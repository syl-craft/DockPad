using DockPad.Services;
using DockPad.Services.Localization;

namespace DockPad.Tests;

public class TileLockStateTests
{
    [Fact]
    public void ParDefaut_LeDeplacementEstVerrouille()
    {
        // Sûr par défaut : la raison d'être du verrou est d'empêcher un déplacement involontaire.
        var state = new TileLockState();

        Assert.False(state.IsUnlocked);
        Assert.Equal("🔒", state.Glyph);
        Assert.Equal(Loc.T("Quick_TileLock_Locked"), state.Tooltip);
    }

    [Fact]
    public void Toggle_DeverrouilleEtLeBoutonDevientUneValidation()
    {
        var state = new TileLockState();

        state.Toggle();

        Assert.True(state.IsUnlocked);
        Assert.Equal("✓", state.Glyph);
        Assert.Equal(Loc.T("Quick_TileLock_Unlocked"), state.Tooltip);
    }

    [Fact]
    public void Toggle_DeuxFois_RevientAuVerrou()
    {
        var state = new TileLockState();

        state.Toggle();
        state.Toggle();

        Assert.False(state.IsUnlocked);
    }

    [Fact]
    public void Lock_DepuisDeverrouille_RemetLeVerrou()
    {
        // C'est ce que fait le masquage ou la réduction de la fenêtre : on ne peut pas oublier de
        // re-verrouiller, parce que ranger la fenêtre le fait à notre place.
        var state = new TileLockState();
        state.Toggle();

        state.Lock();

        Assert.False(state.IsUnlocked);
    }

    [Fact]
    public void Lock_DejaVerrouille_NeChangeRien()
    {
        // Le verrou est posé à chaque passage du cycle de vie de la fenêtre, y compris quand elle
        // était déjà rangée : l'opération doit être idempotente.
        var state = new TileLockState();

        state.Lock();
        state.Lock();

        Assert.False(state.IsUnlocked);
    }
}
