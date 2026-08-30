using System.Globalization;
using DockPad.Secrets;
using DockPad.Services.Localization;

namespace DockPad.Tests.Secrets;

/// <summary>
/// Les deux décisions de l'orchestration qui ne demandent ni coffre ni réseau.
/// </summary>
public class SecretInjectionServiceTests
{
    public SecretInjectionServiceTests() => Loc.SetCulture(CultureInfo.GetCultureInfo("fr"));

    // ───────────── Le statut du coffre ─────────────

    [Fact]
    public void UnCoffreNonConnecte_ArreteAvantDeDemanderUnMotDePasse()
    {
        // Réclamer un mot de passe maître qui ne servirait à rien est le pire des accueils : c'est
        // « bw login » qu'il faut lancer, une fois.
        Assert.Equal(Loc.T("Inject_Error_NotLoggedIn"),
            BitwardenSecretSource.StatusFailure("unauthenticated"));
    }

    [Fact]
    public void UnCoffreVerrouille_MeneAuDeverrouillage()
    {
        Assert.Null(BitwardenSecretSource.StatusFailure("locked"));
    }

    [Fact]
    public void UnCoffreAnnonceDeverrouille_MeneQuandMemeAuDeverrouillage()
    {
        // Sans clé de session, la CLI ne peut rien lire, quoi qu'elle annonce. Un seul cas est
        // distingué, et c'est celui qui appelle une autre action de l'utilisateur.
        Assert.Null(BitwardenSecretSource.StatusFailure("unlocked"));
    }

    [Fact]
    public void UnStatutIllisible_NeBloquePas()
    {
        // La CLI a changé de format, ou a répondu autre chose : tenter vaut mieux que refuser sur
        // une lecture dont on n'est pas sûr.
        Assert.Null(BitwardenSecretSource.StatusFailure(null));
    }

    // ───────────── L'organisation ─────────────

    private static readonly BwOrganization[] Orgs =
    [
        new() { Id = "org-1", Name = "Infra maison" },
        new() { Id = "org-2", Name = "Perso" },
    ];

    [Fact]
    public void TrouveLOrganisationParSonNom()
    {
        var (id, failure) = BitwardenSecretSource.ResolveOrganisation(Orgs, "Infra maison");

        Assert.Equal("org-1", id);
        Assert.Null(failure);
    }

    [Fact]
    public void TrouveLOrganisationParSonIdentifiant()
    {
        // Le script d'origine acceptait les deux : un identifiant lève l'ambiguïté quand deux
        // organisations portent le même nom.
        var (id, _) = BitwardenSecretSource.ResolveOrganisation(Orgs, "org-2");

        Assert.Equal("org-2", id);
    }

    [Fact]
    public void SansOrganisationConfiguree_ChercheDansToutLeCoffre()
    {
        var (id, failure) = BitwardenSecretSource.ResolveOrganisation(Orgs, "");

        Assert.Null(id);
        Assert.Null(failure);
    }

    [Fact]
    public void UneOrganisationIntrouvable_ListeCellesQuiExistent()
    {
        // Sans la liste, on ne sait pas si on s'est trompé de nom ou si l'organisation n'a jamais
        // été créée — deux corrections différentes.
        var (id, failure) = BitwardenSecretSource.ResolveOrganisation(Orgs, "Absente");

        Assert.Null(id);
        Assert.Equal(Loc.F("Inject_Error_OrgMissing", "Absente", "Infra maison, Perso"), failure);
    }

    [Fact]
    public void AucuneOrganisationDuTout_LeDitPlutotQueDeMontrerUnVide()
    {
        var (_, failure) = BitwardenSecretSource.ResolveOrganisation([], "Infra maison");

        Assert.Equal(Loc.F("Inject_Error_OrgMissing", "Infra maison", Loc.T("Inject_Error_OrgNone")), failure);
    }
}
