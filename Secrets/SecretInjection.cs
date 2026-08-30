using System.Threading;
using System.Threading.Tasks;
using System.Windows;

namespace DockPad.Secrets;

/// <summary>
/// La façade du dossier : tout ce que le reste de l'application a le droit de connaître.
/// </summary>
/// <remarks>
/// <para>
/// L'invariant du dossier <c>Secrets/</c> est que <b>rien de ce qui voit un secret n'en sort</b>.
/// DockPad étant un assembly unique, <c>internal</c> ne peut pas poser cette frontière : c'est
/// <c>SecretBoundaryGuardTests</c> qui la tient, et cette façade est la surface qu'il autorise.
/// </para>
/// <para>
/// Deux appelants, et deux seulement : <c>App</c> pour le clic droit et la sortie,
/// <c>SettingsDialog</c> pour l'entrée de menu (par <see cref="SecretMenu"/>).
/// </para>
/// </remarks>
public static class SecretInjection
{
    /// <summary>Ouvre la fenêtre d'injection pour ce fichier.</summary>
    public static Window Handle(string filePath)
    {
        var window = new SecretInjectionWindow(filePath);
        window.Show();
        window.Activate();
        return window;
    }

    /// <summary>
    /// Ouvre la fenêtre de synchronisation du cache local de la CLI.
    /// </summary>
    /// <remarks>
    /// Le mot de passe maître est recueilli par la fenêtre du périmètre, jamais par celle des
    /// Options : c'est ce qui permet au bouton d'y vivre sans faire entrer un secret dehors.
    /// </remarks>
    public static void SyncVault(Window owner)
    {
        var window = SecretInjectionWindow.ForSync();
        window.Owner = owner;
        window.ShowDialog();
    }

    /// <summary>
    /// La date de dernière synchronisation du cache, ou <c>null</c> si elle est inconnue.
    /// </summary>
    /// <remarks>
    /// Ne demande aucun mot de passe : <c>bw status</c> lit le cache sans session. C'est ce qui
    /// permet aux Options d'afficher l'âge du cache sans approcher un secret.
    /// </remarks>
    public static Task<DateTime?> LastVaultSyncAsync(CancellationToken token) =>
        SecretInjectionService.LastSyncAsync(token);

    /// <summary>
    /// Un rendu attend-il encore d'être retiré du presse-papier ?
    /// </summary>
    /// <remarks>
    /// L'instance éphémère du repli — pipe injoignable, donc pas de systray — s'en sert pour
    /// <b>différer sa sortie</b> : mourir à la fermeture de la fenêtre déclencherait
    /// <see cref="ClearClipboardNow"/> avant que l'utilisateur ait pu coller quoi que ce soit.
    /// </remarks>
    public static bool IsClipboardArmed => ClipboardGuard.IsArmed;

    /// <summary>Le verrou du presse-papier a bougé — décompte, effacement, ou désarmement.</summary>
    public static event EventHandler? ClipboardChanged
    {
        add => ClipboardGuard.Changed += value;
        remove => ClipboardGuard.Changed -= value;
    }

    /// <summary>
    /// Retire le rendu du presse-papier s'il s'y trouve encore, puis désarme.
    /// </summary>
    /// <remarks>
    /// Appelé à la sortie de l'application : la promesse « ce secret quitte le presse-papier » ne
    /// peut pas dépendre du fait que DockPad vive assez longtemps. Sans effet quand le délai
    /// d'effacement est réglé à zéro — un réglage qui dit « ne pas effacer » ne peut pas effacer
    /// quand même à la fermeture.
    /// </remarks>
    public static void ClearClipboardNow() => ClipboardGuard.ClearNow();
}
