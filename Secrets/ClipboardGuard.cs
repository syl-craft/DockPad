using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Windows;
using System.Windows.Threading;

namespace DockPad.Secrets;

/// <summary>
/// Met le rendu dans le presse-papier, marqué comme confidentiel, et l'en retire.
/// </summary>
/// <remarks>
/// <para>
/// <b>Le minuteur n'appartient pas à la fenêtre.</b> Fermer la fenêtre une fois le texte collé est
/// le geste naturel ; si le minuteur y vivait, ce geste laisserait le secret dans le presse-papier
/// pour toujours. La fenêtre ne fait qu'observer.
/// </para>
/// <para>
/// <b>On ne retient que l'empreinte du texte</b>, jamais le texte : garder le secret en mémoire
/// pendant tout le décompte irait contre l'objet même de l'opération.
/// </para>
/// </remarks>
public static class ClipboardGuard
{
    /// <summary>
    /// Formats enregistrés que Windows connaît, documentés sous <i>Cloud Clipboard and Clipboard
    /// History Formats</i>.
    /// </summary>
    /// <remarks>
    /// Le premier suffit à exclure des deux mécanismes ; les deux autres sont posés parce qu'ils
    /// sont explicites, et qu'une version de Windows qui n'honorerait que ceux-là existerait sans
    /// qu'on le sache.
    /// </remarks>
    private const string ExcludeFromMonitors = "ExcludeClipboardContentFromMonitorProcessing";
    private const string NoHistory = "CanIncludeInClipboardHistory";
    private const string NoCloud = "CanUploadToCloudClipboard";

    private static DispatcherTimer? _timer;
    private static string? _armedFingerprint;

    /// <summary>Le décompte a avancé, ou le verrou vient d'être désarmé.</summary>
    public static event EventHandler? Changed;

    /// <summary>Secondes restantes avant effacement. Zéro quand le verrou est désarmé.</summary>
    public static int SecondsLeft { get; private set; }

    /// <summary>Vrai tant qu'un rendu est susceptible d'être effacé.</summary>
    public static bool IsArmed => _armedFingerprint is not null;

    // ───────────── Décisions pures ─────────────

    /// <summary>Empreinte SHA-256 hexadécimale — ce qu'on garde à la place du texte.</summary>
    public static string Fingerprint(string text) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text))).ToLowerInvariant();

    /// <summary>
    /// Faut-il effacer ? Seulement si le presse-papier porte encore exactement ce qu'on y a mis.
    /// </summary>
    /// <remarks>
    /// Sans cette comparaison, l'échéance détruirait ce que l'utilisateur a copié entre-temps.
    /// C'est ce que fait KeePass, et c'est la différence entre un filet et un piège.
    /// </remarks>
    public static bool ShouldClear(string? currentText, string? armedFingerprint) =>
        armedFingerprint is not null
        && currentText is not null
        && Fingerprint(currentText) == armedFingerprint;

    /// <summary>
    /// L'objet à déposer : le texte, plus les trois formats qui l'excluent de l'historique et de la
    /// synchronisation entre appareils.
    /// </summary>
    /// <remarks>
    /// <b>Un <c>MemoryStream</c> et non un <c>int</c></b> : la documentation demande un DWORD
    /// sérialisé, et <c>DataObject.SetData</c> sérialisait par <c>BinaryFormatter</c>, désactivé
    /// depuis .NET 8. Les quatre octets sont écrits tels quels par le marshaling OLE.
    /// </remarks>
    public static DataObject BuildDataObject(string text)
    {
        var data = new DataObject();
        data.SetData(DataFormats.UnicodeText, text);
        data.SetData(ExcludeFromMonitors, new MemoryStream([0, 0, 0, 0]));
        data.SetData(NoHistory, new MemoryStream([0, 0, 0, 0]));
        data.SetData(NoCloud, new MemoryStream([0, 0, 0, 0]));
        return data;
    }

    // ───────────── Le verrou ─────────────

    /// <summary>
    /// Copie le rendu et arme l'effacement. <paramref name="seconds"/> à zéro copie sans armer.
    /// </summary>
    /// <remarks>
    /// Un nouveau rendu <b>annule et relance</b> : sinon le minuteur du précédent effacerait le
    /// texte du suivant, en plein milieu de son propre délai.
    /// </remarks>
    public static void Arm(string text, int seconds)
    {
        Disarm();

        // `copy: true` demande à OLE de rendre les formats tout de suite : le contenu survit alors
        // à la fin du processus, ce dont l'instance éphémère du repli a besoin.
        Clipboard.SetDataObject(BuildDataObject(text), copy: true);

        if (seconds <= 0)
        {
            // Réglage à zéro : on ne retient aucune empreinte, et la sortie n'effacera rien. Un
            // réglage qui dit « ne pas effacer » ne peut pas effacer quand même à la fermeture.
            Notify();
            return;
        }

        _armedFingerprint = Fingerprint(text);
        SecondsLeft = seconds;

        _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _timer.Tick += (_, _) =>
        {
            SecondsLeft--;
            if (SecondsLeft <= 0) ClearNow();
            else Notify();
        };
        _timer.Start();
        Notify();
    }

    /// <summary>
    /// Efface le presse-papier s'il porte toujours le rendu, puis désarme. Sans effet si le verrou
    /// n'est pas armé.
    /// </summary>
    /// <remarks>
    /// Appelé à l'échéance <b>et</b> à la sortie de l'application : la promesse « ce secret quitte
    /// le presse-papier » ne peut pas dépendre du fait que DockPad vive assez longtemps.
    /// </remarks>
    public static void ClearNow()
    {
        if (_armedFingerprint is null) return;

        try
        {
            var current = Clipboard.ContainsText() ? Clipboard.GetText() : null;
            if (ShouldClear(current, _armedFingerprint)) Clipboard.Clear();
        }
        catch (Exception ex)
        {
            // Le presse-papier peut être tenu par une autre application. On désarme quand même :
            // réessayer indéfiniment garderait l'empreinte en mémoire sans rien y gagner.
            Services.LogService.Warn(ex, "Effacement du presse-papier");
        }

        Disarm();
    }

    /// <summary>
    /// Arrête le décompte <b>en gardant l'empreinte</b> : le filet de sortie efface toujours.
    /// </summary>
    /// <remarks>
    /// <para>
    /// La différence avec <see cref="Disarm"/> porte tout le sens. Désarmer oublie l'empreinte,
    /// donc la sortie de l'application n'effacerait plus rien et le secret resterait dans le
    /// presse-papier <b>indéfiniment</b> — ce n'est pas ce qu'on attend d'un bouton qui dit
    /// seulement « arrêter le décompte ».
    /// </para>
    /// <para>
    /// Ici on gagne du temps sans perdre la garantie : plus de minuteur, mais
    /// <see cref="ClearNow"/> — appelé à la fermeture de DockPad — a toujours de quoi reconnaître
    /// ce qu'il a mis là et l'effacer.
    /// </para>
    /// </remarks>
    public static void Pause()
    {
        if (_armedFingerprint is null) return;

        _timer?.Stop();
        _timer = null;
        SecondsLeft = 0;
        Notify();
    }

    /// <summary>Le décompte a-t-il été arrêté alors qu'un rendu est toujours dans le presse-papier ?</summary>
    public static bool IsPaused => _armedFingerprint is not null && _timer is null;

    /// <summary>Oublie l'empreinte et arrête le minuteur, sans toucher au presse-papier.</summary>
    public static void Disarm()
    {
        _timer?.Stop();
        _timer = null;
        _armedFingerprint = null;
        SecondsLeft = 0;
        Notify();
    }

    private static void Notify() => Changed?.Invoke(null, EventArgs.Empty);
}
