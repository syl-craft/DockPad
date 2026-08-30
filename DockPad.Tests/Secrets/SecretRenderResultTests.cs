using DockPad.Secrets;

namespace DockPad.Tests.Secrets;

/// <summary>
/// Le tout-ou-rien porté par le type, et non par la discipline de l'appelant.
/// </summary>
/// <remarks>
/// Un objet qui porterait à la fois un texte et une liste d'échecs laisserait un appelant lire le
/// texte sans regarder la liste — c'est exactement la panne d'origine, un rendu partiel qu'on croit
/// complet. Ici il n'y a rien à lire tant que le rendu n'a pas réussi.
/// </remarks>
public class SecretRenderResultTests
{
    [Fact]
    public void UnRenduReussi_PorteSonTexteEtSesCompteurs()
    {
        var result = SecretRenderResult.Rendered("image: ntfy\npassword: s3cr3t", markerCount: 2, itemCount: 1);

        Assert.True(result.Ok);
        Assert.Equal("image: ntfy\npassword: s3cr3t", result.Text);
        Assert.Equal(2, result.MarkerCount);
        Assert.Equal(1, result.ItemCount);
        Assert.Empty(result.Failures);
    }

    [Fact]
    public void UnEchec_NeDonneAucunTexte()
    {
        // La garantie centrale : pas de rendu partiel, donc rien à lire du tout.
        var result = SecretRenderResult.Failed(["ntfy : absent"]);

        Assert.False(result.Ok);
        Assert.Throws<InvalidOperationException>(() => result.Text);
    }

    [Fact]
    public void UnEchec_ListeCeQuiAEchoue()
    {
        var result = SecretRenderResult.Failed(["ntfy : absent", "vault → champ 'token' absent ou vide"]);

        Assert.Equal(["ntfy : absent", "vault → champ 'token' absent ou vide"], result.Failures);
    }

    [Fact]
    public void UnEchecSansMotif_EstImpossible()
    {
        // Un échec qui ne dit pas ce qui a échoué ne serait pas actionnable, et laisserait croire
        // à un refus arbitraire.
        Assert.Throws<ArgumentException>(() => SecretRenderResult.Failed([]));
    }
}
