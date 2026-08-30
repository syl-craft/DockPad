using System.Globalization;
using DockPad.Secrets;
using DockPad.Services.Localization;

namespace DockPad.Tests.Secrets;

/// <summary>
/// Le cœur pur : trouver les marqueurs, substituer, et refuser tout ce qui n'est pas complet.
/// </summary>
/// <remarks>
/// La langue est posée explicitement — sans quoi ces tests héritent de celle laissée par une autre
/// classe et passent ou cassent selon l'ordonnancement.
/// </remarks>
public class SecretTemplateTests
{
    public SecretTemplateTests() => Loc.SetCulture(CultureInfo.GetCultureInfo("fr"));

    private static SecretLookup Always(string value) => SecretLookup.Found(value);

    // ───────────── Détection ─────────────

    [Fact]
    public void TrouveUnMarqueurEntoureDEspaces()
    {
        var markers = SecretTemplate.FindMarkers("token: \"{{ bw:ntfy:token }}\"");

        Assert.Equal([new SecretMarker("ntfy", "token")], markers);
    }

    [Fact]
    public void TrouveUnMarqueurSansEspaces()
    {
        var markers = SecretTemplate.FindMarkers("token: \"{{bw:ntfy:token}}\"");

        Assert.Equal([new SecretMarker("ntfy", "token")], markers);
    }

    [Fact]
    public void TrouvePlusieursMarqueursSurLaMemeLigne()
    {
        var markers = SecretTemplate.FindMarkers("{{ bw:a:x }} et {{ bw:b:y }}");

        Assert.Equal([new SecretMarker("a", "x"), new SecretMarker("b", "y")], markers);
    }

    [Fact]
    public void UnNomDItemAvecDesEspaces_EstUnMarqueurValide()
    {
        // Trouvaille de revue. « NAS QNAP » est un nom d'item parfaitement ordinaire, et le coffre
        // le résout très bien — mais la regex excluait l'espace, si bien que le marqueur n'était
        // pas vu du tout et que l'utilisateur s'entendait dire « aucun marqueur » devant un fichier
        // qui n'en manquait pas.
        var markers = SecretTemplate.FindMarkers("token: \"{{ bw:NAS QNAP:token }}\"");

        Assert.Equal([new SecretMarker("NAS QNAP", "token")], markers);
    }

    [Fact]
    public void IgnoreUneAccoladeDoubleQuiNEstPasUnMarqueurBw()
    {
        // Un gabarit Go ou Jinja n'est pas un marqueur : la détection ne doit pas le revendiquer.
        var markers = SecretTemplate.FindMarkers("{{ .Values.image }}");

        Assert.Empty(markers);
    }

    [Fact]
    public void UnFichierSansMarqueur_NeDonneRien()
    {
        Assert.Empty(SecretTemplate.FindMarkers("image: ntfy/ntfy:latest"));
    }

    // ───────────── Substitution ─────────────

    [Fact]
    public void RemplaceToutesLesOccurrencesDUnMemeMarqueur()
    {
        var result = SecretTemplate.Render(
            "a: \"{{ bw:ntfy:token }}\"\nb: \"{{ bw:ntfy:token }}\"", _ => Always("tk_42"));

        Assert.True(result.Ok);
        Assert.Equal("a: \"tk_42\"\nb: \"tk_42\"", result.Text);
    }

    [Fact]
    public void CompteLesMarqueursEtLesItemsDistincts()
    {
        // Deux marqueurs sur le même item ne font qu'un item lu — c'est ce que compte le
        // compte-rendu, et ce que comptait le cache du script d'origine.
        var result = SecretTemplate.Render(
            "{{ bw:ntfy:token }} {{ bw:ntfy:password }} {{ bw:vault:password }}",
            _ => Always("v"));

        Assert.Equal(3, result.MarkerCount);
        Assert.Equal(2, result.ItemCount);
    }

    [Fact]
    public void LaisseLeTexteAutourIntact()
    {
        var result = SecretTemplate.Render(
            "services:\n  ntfy:\n    token: \"{{ bw:ntfy:token }}\"\n", _ => Always("tk"));

        Assert.Equal("services:\n  ntfy:\n    token: \"tk\"\n", result.Text);
    }

    // ───────────── Premier filet : tout ou rien ─────────────

    [Fact]
    public void UnSeulMarqueurNonResolu_AnnuleToutLeRendu()
    {
        var result = SecretTemplate.Render(
            "{{ bw:ok:token }} {{ bw:absent:token }}",
            m => m.Item == "ok" ? Always("v") : SecretLookup.Missing("absent du coffre"));

        Assert.False(result.Ok);
    }

    [Fact]
    public void CollecteTousLesEchecs_PasSeulementLePremier()
    {
        // Corriger un marqueur à la fois, en relançant à chaque coup, serait une perte de temps
        // pure : le premier écran doit montrer tout ce qu'il y a à corriger.
        var result = SecretTemplate.Render(
            "{{ bw:a:x }} {{ bw:b:y }}", m => SecretLookup.Missing($"{m.Item} introuvable"));

        Assert.Equal(2, result.Failures.Count);
    }

    [Fact]
    public void UneValeurVide_EstUnEchecEtNonUneValeur()
    {
        var result = SecretTemplate.Render("{{ bw:ntfy:token }}", _ => SecretLookup.Found(""));

        Assert.False(result.Ok);
    }

    [Fact]
    public void UnFichierSansMarqueur_EstUnRefus()
    {
        // Ce n'est pas un cas neutre : c'est le signe qu'on a visé le mauvais fichier.
        var result = SecretTemplate.Render("image: ntfy/ntfy:latest", _ => Always("v"));

        Assert.False(result.Ok);
        Assert.Equal([Loc.T("Inject_Error_NoMarkers")], result.Failures);
    }

    // ───────────── Second filet : le balayage final ─────────────

    [Fact]
    public void UneValeurDuCoffreQuiPorteUnMarqueur_FaitEchouerLeRendu()
    {
        // Le second filet ne fait pas confiance au premier : il regarde le résultat, quelle que
        // soit l'origine de ce qui y traîne.
        var result = SecretTemplate.Render("{{ bw:ntfy:token }}", _ => Always("{{ oups }}"));

        Assert.False(result.Ok);
    }

    [Fact]
    public void UnRemplacerSurvivant_FaitEchouerLeRendu()
    {
        var result = SecretTemplate.Render("a: REMPLACER\nb: \"{{ bw:ntfy:token }}\"", _ => Always("tk"));

        Assert.False(result.Ok);
    }

    [Fact]
    public void LeMessageDeRestesNeRecopieRienDuTexteRendu()
    {
        // Trouvaille de revue. Le balayage porte sur le texte SUBSTITUÉ : une valeur du coffre qui
        // contient elle-même des accolades — une note portant un gabarit, un mot de passe à
        // accolades — voyait ce fragment interpolé dans le message et affiché à l'écran. Partout
        // ailleurs le périmètre ne sort que des noms et des nombres.
        var result = SecretTemplate.Render("x: \"{{ bw:ntfy:token }}\"", _ => Always("{{ secret-tres-reconnaissable }}"));

        Assert.False(result.Ok);
        Assert.DoesNotContain(result.Failures, f => f.Contains("reconnaissable", StringComparison.Ordinal));
    }

    [Fact]
    public void LeBalayageRejetteToutGabaritEtranger()
    {
        // Conséquence assumée : un gabarit Go légitime dans le même fichier fait échouer le rendu.
        // Le filet ne sait pas distinguer, et il se trompe du bon côté.
        var leftovers = SecretTemplate.FindLeftovers("image: \"{{ .Values.tag }}\"");

        Assert.Equal(["{{ .Values.tag }}"], leftovers);
    }

    [Fact]
    public void UnTexteRenduPropre_NeLaisseAucunReste()
    {
        Assert.Empty(SecretTemplate.FindLeftovers("token: \"tk_42\""));
    }

    // ───────────── Échappement ─────────────
    //
    // Un fichier peut vouloir MONTRER la syntaxe sans la subir — un CLAUDE.md, un README. Un
    // antislash devant les accolades le dit. Ce qui rend ces tests nécessaires plutôt que
    // décoratifs, c'est que le second filet rejette tout « {{ … }} » survivant : un marqueur
    // échappé en produit un, et se ferait rejeter par le filet censé nous protéger. L'ordre des
    // quatre étapes de Render est la seule chose qui rend les deux compatibles.

    [Fact]
    public void UnMarqueurEchappeNestPasUnMarqueur()
    {
        var markers = SecretTemplate.FindMarkers(@"exemple : \{{ bw:ntfy:token }}");

        Assert.Empty(markers);
    }

    [Fact]
    public void UnMarqueurEchappeRessortLitteralSansSonAntislash()
    {
        var result = SecretTemplate.Render(
            "vrai: {{ bw:ntfy:token }}\ndoc:  " + @"\{{ bw:item:champ }}",
            _ => Always("s3cret"));

        Assert.True(result.Ok);
        Assert.Equal("vrai: s3cret\ndoc:  {{ bw:item:champ }}", result.Text);
    }

    /// <summary>Le second filet ne doit pas mordre sur ce que l'échappement produit légitimement.</summary>
    [Fact]
    public void UnMarqueurEchappeNeDeclenchePasLeSecondFilet()
        => Assert.Empty(SecretTemplate.FindLeftovers(@"doc : \{{ bw:item:champ }}"));

    /// <summary>
    /// Le test qui compte : l'échappement ne perce pas le filet. Un « {{ … }} » <b>non</b> échappé
    /// fait toujours échouer le rendu, même quand un échappé le côtoie.
    /// </summary>
    [Fact]
    public void UnResteNonEchappeEchoueTouJoursMemeAuxCotesDUnEchappe()
    {
        var result = SecretTemplate.Render(
            "vrai: {{ bw:ntfy:token }}\ndoc:  " + @"\{{ bw:item:champ }}",
            _ => Always("{{ oups }}"));

        Assert.False(result.Ok);
    }

    /// <summary>
    /// Un fichier qui ne fait que documenter la syntaxe n'a rien à produire — le même refus que
    /// devant un fichier sans marqueur, et c'est la bonne réponse : on a visé un README.
    /// </summary>
    [Fact]
    public void UnFichierEntierementEchappeNaAucunMarqueur()
    {
        var result = SecretTemplate.Render(@"doc : \{{ bw:item:champ }}", _ => Always("x"));

        Assert.False(result.Ok);
        Assert.Contains(result.Failures, f => f.Contains("marqueur", StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// L'antislash est toujours consommé, même doublé : je n'ajoute pas de règle de doublement,
    /// ce serait une seconde syntaxe à retenir pour un cas que personne n'a.
    /// </summary>
    [Fact]
    public void UnAntislashDoubleNeResoutToujoursPas()
    {
        var result = SecretTemplate.Render(
            "vrai: {{ bw:ntfy:token }}\ndoc:  " + @"\\{{ bw:item:champ }}",
            _ => Always("s3cret"));

        Assert.True(result.Ok);
        Assert.EndsWith(@"\{{ bw:item:champ }}", result.Text);
    }
}
