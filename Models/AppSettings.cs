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

    /// <summary>Modificateurs du raccourci global. Zéro = aucun réglage, le défaut s'applique.</summary>
    public int HotkeyModifiers { get; set; }

    /// <summary>Touche du raccourci global. Zéro = aucun réglage, le défaut s'applique.</summary>
    public int HotkeyKey { get; set; }
}
