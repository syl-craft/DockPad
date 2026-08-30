namespace DockPad.Models;

/// <summary>
/// Options de l'application, telles qu'elles vivent dans <c>settings.json</c>.
/// </summary>
/// <remarks>
/// <para>
/// Chaque valeur porte son défaut ici, et non au point de lecture : une clé absente du fichier —
/// version plus ancienne, fichier écrit à la main — doit donner le comportement attendu et non
/// <c>null</c> ou <c>false</c>. C'est ce que fait <c>System.Text.Json</c>, qui laisse
/// l'initialiseur en place quand la propriété manque.
/// </para>
/// <para>
/// <b>Le démarrage automatique n'est pas ici</b> : c'est une entrée de la clé <c>Run</c> de
/// Windows, elle n'a pas d'équivalent en fichier. <c>SettingsService</c> la garde dans le registre.
/// </para>
/// </remarks>
public class AppSettings
{
    /// <summary>Étiquette de langue, ou vide pour « suivre Windows ».</summary>
    public string Language { get; set; } = "";

    /// <summary><c>Light</c>, <c>Dark</c>, ou vide pour « suivre Windows ».</summary>
    public string Theme { get; set; } = "";

    /// <summary>Modificateur de la moitié gauche de l'overlay, ou vide pour « automatique ».</summary>
    public string TriggerFirst { get; set; } = "";

    /// <summary>Modificateur de la moitié droite de l'overlay, ou vide pour « automatique ».</summary>
    public string TriggerSecond { get; set; } = "";

    /// <summary>Arguments passés à <c>claude</c> par le prédéfini « Ouvrir un terminal Claude ».</summary>
    public string ClaudeArgs { get; set; } = "";

    /// <summary>
    /// Télécharger l'icône du site pour les tuiles web. Vrai par défaut : un réglage réseau qu'on
    /// n'a jamais vu ne peut pas avoir été refusé.
    /// </summary>
    public bool AutoFavicon { get; set; } = true;

    /// <summary>
    /// Chemin de <c>bw.exe</c>, ou vide pour laisser DockPad le chercher.
    /// </summary>
    /// <remarks>
    /// Un chemin réglé qui n'existe plus retombe sur la détection : le dossier d'installation
    /// WinGet porte un identifiant de version, donc la valeur enregistrée devient fausse à la
    /// première mise à jour de la CLI.
    /// </remarks>
    public string BitwardenCliPath { get; set; } = "";

    /// <summary>
    /// Délai avant effacement du presse-papier après une injection. Zéro désactive.
    /// </summary>
    /// <remarks>
    /// 90 et non 30 : la cible de collage est une interface web dans un navigateur, il faut le
    /// temps de trouver l'onglet et d'ouvrir la bonne page.
    /// </remarks>
    public int ClipboardClearSeconds { get; set; } = 90;

    /// <summary>
    /// Organisation Vaultwarden où chercher les items, ou vide pour chercher dans tout le coffre.
    /// </summary>
    /// <remarks>
    /// Vaultwarden n'a qu'un coffre par compte : sans ce cantonnement, un item personnel du même
    /// nom rend la résolution ambiguë.
    /// </remarks>
    public string VaultOrganization { get; set; } = "";

    /// <summary>
    /// Synchroniser le cache de la CLI juste avant de lire le coffre.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Coché par défaut, et c'est un renversement.</b> La règle d'origine — « pas de sync avant
    /// chaque injection, ce serait un aller-retour réseau pour un coffre qui bouge rarement » —
    /// avait tort sur son propre critère : le coffre bouge <i>précisément</i> quand on injecte, on
    /// vient d'y déposer la valeur qu'on veut déployer.
    /// </para>
    /// <para>
    /// Le prix de l'oubli n'est pas une seconde d'attente, c'est un <b>secret périmé déposé sur le
    /// NAS</b>, découvert bien plus tard et loin d'ici. Le piège a mordu trois fois. Qui trouve ça
    /// lent décoche — et c'est alors son choix, explicite, au lieu d'un silence.
    /// </para>
    /// </remarks>
    public bool SyncVaultBeforeInject { get; set; } = true;

    /// <summary>Modificateurs du raccourci global. Zéro = aucun réglage, le défaut s'applique.</summary>
    public int HotkeyModifiers { get; set; }

    /// <summary>Touche du raccourci global. Zéro = aucun réglage, le défaut s'applique.</summary>
    public int HotkeyKey { get; set; }
}
