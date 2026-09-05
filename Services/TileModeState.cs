using DockPad.Models;

namespace DockPad.Services;

/// <summary>
/// Laquelle des deux grilles la fenêtre affiche : les raccourcis, ou les favoris.
/// </summary>
/// <remarks>
/// <para>
/// L'état vit ici et non dans le code-behind, pour la même raison que <see cref="TileLockState"/> :
/// le glyphe, l'infobulle et la cible sont des décisions, elles se testent sans WPF. Le code-behind
/// branche le clic, lit trois propriétés et choisit un style.
/// </para>
/// <para>
/// <b>Rien n'est écrit sur le disque</b>, et ranger la fenêtre repose le mode (voir
/// <see cref="Reset"/>) : le mode Favoris est un détour, pas un réglage. Le retrouver deux jours
/// plus tard sans savoir pourquoi la grille a changé serait déroutant, et un réglage qui se règle
/// tout seul est un réglage qu'on n'a pas voulu.
/// </para>
/// </remarks>
public sealed class TileModeState
{
    /// <summary>La grille affichée est celle des favoris.</summary>
    public bool IsFavorites { get; private set; }

    /// <summary>La cible à lire et à écrire — ce que la fenêtre passe à tous les services.</summary>
    public TileTarget Target => IsFavorites ? TileTarget.Favorites : TileTarget.Shortcuts;

    /// <summary>Bascule le mode : c'est le clic sur le bouton de la toolbar.</summary>
    public void Toggle() => IsFavorites = !IsFavorites;

    /// <summary>
    /// Revient aux raccourcis. Appelé quand la fenêtre est masquée ou réduite, et sans effet si
    /// l'on y est déjà — le cycle de vie de la fenêtre passe ici plusieurs fois.
    /// </summary>
    public void Reset() => IsFavorites = false;

    /// <summary>
    /// Glyphe du bouton : il dit le mode <b>affiché</b>, comme le cadenas du verrou dit l'état posé.
    /// </summary>
    public string Glyph => IsFavorites ? "★" : "▦";

    /// <summary>Infobulle du bouton : elle nomme l'action, là où le glyphe dit l'état.</summary>
    public string Tooltip => IsFavorites
        ? Loc.T("Quick_TileMode_ToShortcuts")
        : Loc.T("Quick_TileMode_ToFavorites");
}
