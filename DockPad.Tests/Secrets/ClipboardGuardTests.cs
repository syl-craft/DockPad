using System.IO;
using DockPad.Secrets;

namespace DockPad.Tests.Secrets;

/// <summary>
/// L'empreinte, la décision d'effacer, et le marquage qui tient le secret hors de l'historique.
/// </summary>
/// <remarks>
/// Aucun de ces tests ne touche au presse-papier de la machine : ils portent sur les décisions et
/// sur l'objet construit avant la copie.
/// </remarks>
public class ClipboardGuardTests
{
    // ───────────── L'empreinte ─────────────

    [Fact]
    public void LaMemeChaineDonneLaMemeEmpreinte()
    {
        Assert.Equal(ClipboardGuard.Fingerprint("secret"), ClipboardGuard.Fingerprint("secret"));
    }

    [Fact]
    public void DeuxChainesDifferentesDonnentDeuxEmpreintes()
    {
        Assert.NotEqual(ClipboardGuard.Fingerprint("secret"), ClipboardGuard.Fingerprint("secrez"));
    }

    [Fact]
    public void LEmpreinteNePorteRienDuTexte()
    {
        // On retient l'empreinte précisément pour ne pas garder le secret en mémoire le temps du
        // décompte : un SHA-256 en hexadécimal, et rien d'autre.
        var fingerprint = ClipboardGuard.Fingerprint("mot-de-passe-très-reconnaissable");

        Assert.Equal(64, fingerprint.Length);
        Assert.DoesNotContain("passe", fingerprint, StringComparison.OrdinalIgnoreCase);
    }

    // ───────────── La décision d'effacer ─────────────

    [Fact]
    public void EffaceSiLePressePapierPorteToujoursCeQuOnYAMis()
    {
        var armed = ClipboardGuard.Fingerprint("rendu");

        Assert.True(ClipboardGuard.ShouldClear("rendu", armed));
    }

    [Fact]
    public void NEffacePasSiLUtilisateurACopieAutreChose()
    {
        // C'est ce que fait KeePass : effacer sans regarder détruirait des données qui ne sont pas
        // les nôtres.
        var armed = ClipboardGuard.Fingerprint("rendu");

        Assert.False(ClipboardGuard.ShouldClear("une adresse copiée entre-temps", armed));
    }

    [Fact]
    public void NEffacePasUnPressePapierSansTexte()
    {
        // Une image ou un fichier copié entre-temps : il n'y a pas de texte, donc rien qui soit à
        // nous.
        Assert.False(ClipboardGuard.ShouldClear(null, ClipboardGuard.Fingerprint("rendu")));
    }

    [Fact]
    public void NEffaceRienSansEmpreinteArmee()
    {
        // Verrou désarmé — réglage à zéro, ou rien n'a été rendu depuis le démarrage.
        Assert.False(ClipboardGuard.ShouldClear("n'importe quoi", armedFingerprint: null));
    }

    // ───────────── Le marquage du presse-papier ─────────────

    [Fact]
    public void LObjetCopiePorteLeTexte()
    {
        var data = ClipboardGuard.BuildDataObject("services:\n  ntfy:");

        Assert.Equal("services:\n  ntfy:", data.GetData(System.Windows.DataFormats.UnicodeText));
    }

    [Fact]
    public void LObjetCopiePorteLesTroisFormatsDeConfidentialite()
    {
        // Vider le presse-papier après un délai ne suffit pas : sans ces trois formats, le secret
        // survit en clair dans Win+V et part sur les autres appareils de l'utilisateur.
        var data = ClipboardGuard.BuildDataObject("secret");

        Assert.True(data.GetDataPresent("ExcludeClipboardContentFromMonitorProcessing"));
        Assert.True(data.GetDataPresent("CanIncludeInClipboardHistory"));
        Assert.True(data.GetDataPresent("CanUploadToCloudClipboard"));
    }

    [Fact]
    public void LesDeuxDrapeauxValentZero()
    {
        // La documentation Microsoft demande un DWORD sérialisé à zéro. Un `int` passerait par
        // BinaryFormatter, désactivé depuis .NET 8 : d'où les quatre octets d'un MemoryStream.
        var data = ClipboardGuard.BuildDataObject("secret");

        foreach (var format in new[] { "CanIncludeInClipboardHistory", "CanUploadToCloudClipboard" })
        {
            var stream = Assert.IsType<MemoryStream>(data.GetData(format));
            Assert.Equal(new byte[] { 0, 0, 0, 0 }, stream.ToArray());
        }
    }

    // ───────────── Arrêter le décompte sans désarmer ─────────────

    /// <summary>
    /// <c>Pause</c> sur un verrou non armé ne fait rien, et ne lève pas.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Ce que ce test NE couvre pas, et pourquoi.</b> La propriété qui compte — <c>Pause</c>
    /// <i>conserve</i> l'empreinte là où <c>Disarm</c> l'oublie, donc l'effacement de sortie mord
    /// encore — exige d'armer, et <c>Arm</c> écrit dans le <b>vrai</b> presse-papier de la machine.
    /// Un test qui le ferait écraserait ce que le développeur y avait mis.
    /// </para>
    /// <para>
    /// La distinction est donc portée par le type (<c>IsArmed</c> suit l'empreinte, pas le
    /// minuteur) et par la documentation, pas par un test automatique. C'est une lacune assumée et
    /// nommée plutôt qu'un test de façade qui n'exercerait rien.
    /// </para>
    /// </remarks>
    [Fact]
    public void ArreterLeDecompte_SansArmement_NeFaitRien()
    {
        ClipboardGuard.Pause();

        Assert.False(ClipboardGuard.IsArmed);
        Assert.False(ClipboardGuard.IsPaused);
    }

    /// <summary>Un verrou non armé n'est pas « en pause » : les deux états sont distincts.</summary>
    [Fact]
    public void UnVerrouNonArme_NEstPasEnPause()
        => Assert.False(ClipboardGuard.IsPaused);
}
