namespace DockPad.Services;

/// <summary>
/// Verrou du déplacement des tuiles : tant qu'il est posé, aucun glisser-déposer entre tuiles ne
/// démarre.
/// </summary>
/// <remarks>
/// <para>
/// Le clic sur une tuile lance son action, et le même geste manqué de quelques pixels la déplaçait.
/// Le verrou fait de la réorganisation un mode qu'on demande, plutôt qu'un accident possible à
/// chaque clic.
/// </para>
/// <para>
/// <b>Le verrou ne ferme qu'une porte</b> : le glissement interne entre tuiles. Déposer un dossier
/// ou un <c>.url</c> depuis l'Explorateur, « ↗ Déplacer vers la page » du clic droit et la
/// réorganisation des pages restent ouverts — ce sont des gestes délibérés, qu'on ne déclenche pas
/// en visant une tuile.
/// </para>
/// <para>
/// <b>Rien n'est écrit sur le disque</b>, et ranger la fenêtre repose le verrou (voir
/// <see cref="Lock"/>) : un état déverrouillé qui survivrait à un redémarrage annulerait la
/// protection sans que personne s'en souvienne.
/// </para>
/// <para>
/// L'état vit ici et non dans le code-behind pour la même raison que <c>UsageViewModel</c> : le
/// glyphe et l'infobulle sont des décisions, elles se testent sans WPF.
/// </para>
/// </remarks>
public sealed class TileLockState
{
    /// <summary>Le déplacement des tuiles est autorisé.</summary>
    public bool IsUnlocked { get; private set; }

    /// <summary>Bascule le verrou : c'est le clic sur le bouton de la toolbar.</summary>
    public void Toggle() => IsUnlocked = !IsUnlocked;

    /// <summary>
    /// Repose le verrou. Appelé quand la fenêtre est masquée ou réduite, et sans effet si le verrou
    /// est déjà posé — le cycle de vie de la fenêtre passe ici plusieurs fois.
    /// </summary>
    public void Lock() => IsUnlocked = false;

    /// <summary>
    /// Glyphe du bouton. Verrouillé : le cadenas dit de quoi il s'agit. Déverrouillé : une coche,
    /// parce que le bouton ne sert plus qu'à annoncer qu'on a fini de déplacer.
    /// </summary>
    public string Glyph => IsUnlocked ? "✓" : "🔒";

    /// <summary>Infobulle du bouton : elle nomme l'action, là où le glyphe dit l'état.</summary>
    public string Tooltip => IsUnlocked
        ? "Terminer — le déplacement des tuiles est actif"
        : "Déverrouiller le déplacement des tuiles";
}
