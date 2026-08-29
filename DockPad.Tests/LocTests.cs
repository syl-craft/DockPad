using System.Globalization;
using System.Threading.Tasks;
using DockPad.Services.Localization;

namespace DockPad.Tests;

/// <summary>
/// Résolution des chaînes, culture courante et repli. Ces tests forcent la culture à chaque fois :
/// un test de localisation qui dépend de la langue de la machine est un test qui passe chez son
/// auteur et casse ailleurs.
/// </summary>
public class LocTests
{
    [Fact]
    public void T_EnAnglais_RendLaChaineNeutre()
    {
        Loc.SetCulture(CultureInfo.GetCultureInfo("en"));

        Assert.Equal("Cancel", Loc.T("Common_Cancel"));
    }

    [Fact]
    public void T_EnFrancais_RendLeSatellite()
    {
        Loc.SetCulture(CultureInfo.GetCultureInfo("fr"));

        Assert.Equal("Annuler", Loc.T("Common_Cancel"));
    }

    [Fact]
    public void T_LangueNonTraduite_RetombeSurLAnglais()
    {
        // Le repli de ResourceManager remonte à la langue neutre. C'est la raison pour laquelle le
        // neutre est l'anglais et non un dépotoir de clés : c'est ce que verra un poste japonais.
        Loc.SetCulture(CultureInfo.GetCultureInfo("ja"));

        Assert.Equal("Cancel", Loc.T("Common_Cancel"));
    }

    [Fact]
    public void T_CleInconnue_RendLaCleEntreCrochets()
    {
        // Jamais d'exception à l'écran : une clé oubliée doit se voir sans casser la fenêtre.
        Assert.Equal("[Nope_Missing]", Loc.T("Nope_Missing"));
    }

    [Fact]
    public void SetCulture_Null_SuitLaLangueDeWindows()
    {
        // Le contrat, portable : si Windows est en français ou en anglais, automatique donne cette
        // langue ; sinon il retombe sur l'anglais neutre.
        var systeme = CultureInfo.InstalledUICulture.TwoLetterISOLanguageName;
        var attendu = systeme is "fr" or "en" ? systeme : "en";

        Loc.SetCulture(null);

        Assert.Equal(attendu, Loc.Current.TwoLetterISOLanguageName);
    }

    [Fact]
    public void SetCulture_Null_NeDependPasDeLaLangueChoisieAvant()
    {
        // Le piège : SetCulture écrit dans CurrentUICulture. Le lire pour résoudre « automatique »
        // faisait garder la dernière langue choisie — automatique n'y revenait jamais.
        Loc.SetCulture(CultureInfo.GetCultureInfo("en"));
        Loc.SetCulture(null);
        var apresAnglais = Loc.Current;

        Loc.SetCulture(CultureInfo.GetCultureInfo("fr"));
        Loc.SetCulture(null);

        Assert.Equal(apresAnglais, Loc.Current);
    }

    [Fact]
    public async Task SetCulture_PoseAussiLaCultureDesThreadsDArrierePlan()
    {
        // Sans DefaultThreadCurrentCulture, les Task.Run des fournisseurs de consommation formatent
        // dans la culture d'origine du processus : les jetons et les heures seraient en français
        // sous une interface anglaise.
        Loc.SetCulture(CultureInfo.GetCultureInfo("en"));

        var surArrierePlan = await Task.Run(() => CultureInfo.CurrentCulture.TwoLetterISOLanguageName);

        Assert.Equal("en", surArrierePlan);
    }

    [Fact]
    public void SetCulture_NotifieLIndexeur()
    {
        // « Item[] » invalide toutes les liaisons d'indexeur de l'application d'un seul coup :
        // c'est tout le mécanisme de la bascule à chaud, et l'extension de balisage n'en est que
        // le raccourci d'écriture.
        Loc.SetCulture(CultureInfo.GetCultureInfo("fr"));
        var vus = new List<string?>();
        Loc.Instance.PropertyChanged += (_, e) => vus.Add(e.PropertyName);

        Loc.SetCulture(CultureInfo.GetCultureInfo("en"));

        Assert.Contains("Item[]", vus);
    }

    [Fact]
    public void Indexeur_RendLaMemeChoseQueT()
    {
        // La liaison XAML passe par l'indexeur, le code par T : deux portes, une seule vérité.
        Loc.SetCulture(CultureInfo.GetCultureInfo("fr"));

        Assert.Equal(Loc.T("Common_Save"), Loc.Instance["Common_Save"]);
    }

    [Theory]
    [InlineData("", null)]
    [InlineData("fr", "fr")]
    [InlineData("en", "en")]
    [InlineData("zz-ZZ", null)]
    public void Parse_RendLaCultureOuNullPourAutomatique(string tag, string? attendu)
    {
        // Une étiquette invalide vaut « automatique », jamais une exception : la valeur vient du
        // registre, qu'un utilisateur peut éditer à la main.
        Assert.Equal(attendu, Loc.Parse(tag)?.TwoLetterISOLanguageName);
    }

    // ---------------------------------------------------------------- pseudo-langue « 1337 »

    /// <summary>
    /// <c>qps-Ploc</c> est la seule étiquette qui donne une culture distincte.
    /// </summary>
    /// <remarks>
    /// Les deux formes qu'on écrit d'instinct échouent en silence, ce qui est le pire des cas :
    /// <c>fr-x-1337</c> retombe sur <c>fr</c> et <c>x-leet</c> sur la culture invariante. La langue
    /// aurait été indistinguable de son original, sans le moindre message.
    /// </remarks>
    [Fact]
    public void Pseudo_EstUneCultureDistincte()
    {
        Assert.Equal("qps-Ploc", Loc.Pseudo.Name);
        Assert.NotEqual("fr", CultureInfo.GetCultureInfo("fr-x-1337").Name is "fr" ? "autre" : "fr");
        Assert.Equal("fr", CultureInfo.GetCultureInfo("fr-x-1337").Name);
        Assert.Equal("", CultureInfo.GetCultureInfo("x-leet").Name);
    }

    [Fact]
    public void Parse_AccepteLaPseudoLangue()
        => Assert.Equal("qps-Ploc", Loc.Parse("qps-Ploc")?.Name);

    /// <summary>
    /// Le texte « 1337 » est du français aux glyphes substitués : sa grammaire est française.
    /// </summary>
    /// <remarks>
    /// Sans cette séparation, SmartFormat chercherait une règle de pluriel pour <c>qps</c>, n'en
    /// trouverait aucune, et lèverait — son formateur est réglé sur « lever plutôt que passer
    /// inaperçu ». Les quarante-deux gabarits du magasin seraient tombés.
    /// </remarks>
    [Fact]
    public void Formatting_EstFrancaisePourLaPseudoLangue()
    {
        Loc.SetCulture(Loc.Pseudo);
        try
        {
            Assert.Equal("qps-Ploc", Loc.Current.Name);
            Assert.Equal("fr", Loc.Formatting.TwoLetterISOLanguageName);
            // La culture du thread suit les règles, pas le fichier de ressources.
            Assert.Equal("qps-Ploc", CultureInfo.CurrentUICulture.Name);
            Assert.Equal("fr", CultureInfo.CurrentCulture.TwoLetterISOLanguageName);
        }
        finally { Loc.SetCulture(CultureInfo.GetCultureInfo("fr")); }
    }

    [Fact]
    public void Formatting_EstLaCultureCouranteAilleurs()
    {
        Loc.SetCulture(CultureInfo.GetCultureInfo("en"));
        try { Assert.Equal("en", Loc.Formatting.Name); }
        finally { Loc.SetCulture(CultureInfo.GetCultureInfo("fr")); }
    }

    /// <summary>Le pluriel français doit s'appliquer au texte leet, qui en dérive.</summary>
    [Fact]
    public void Pluriel_SuitLeFrancaisEnPseudoLangue()
    {
        Loc.SetCulture(Loc.Pseudo);
        try
        {
            // Règle française : 0 et 1 prennent le singulier. Le gabarit vient du magasin leet.
            var zero = Loc.Formatter.Format(Loc.Formatting, "{0} {0:plural:r3gl3|r3gl35}", 0);
            var deux = Loc.Formatter.Format(Loc.Formatting, "{0} {0:plural:r3gl3|r3gl35}", 2);

            Assert.Equal("0 r3gl3", zero);
            Assert.Equal("2 r3gl35", deux);
        }
        finally { Loc.SetCulture(CultureInfo.GetCultureInfo("fr")); }
    }
}
