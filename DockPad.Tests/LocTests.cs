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
}
