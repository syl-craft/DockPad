using System.IO;
using System.Net;
using System.Net.Http;
using DockPad.Models;
using DockPad.Services;

namespace DockPad.Tests;

/// <summary>
/// Téléchargement automatique du favicon d'une tuile web.
/// </summary>
/// <remarks>
/// Aucun test ne sort sur le réseau : le <see cref="HttpMessageHandler"/> est factice, comme pour
/// <c>ClaudeLimitsClient</c>. Ce qui est vérifié ici, ce sont les deux choses qui peuvent se
/// tromper en silence — ce qu'on envoie au service tiers, et quand on décide de ne rien envoyer.
/// </remarks>
public class FaviconServiceTests
{
    // ---------------------------------------------------------------- domaine

    [Theory]
    [InlineData("https://music.youtube.com/", "music.youtube.com")]
    [InlineData("https://admin.wiliwo.com/clients/42?tri=nom", "admin.wiliwo.com")]
    [InlineData("http://localhost:44351/api", "localhost")]
    public void DomainOf_rend_l_hote(string url, string expected)
        => Assert.Equal(expected, FaviconService.DomainOf(url));

    [Theory]
    [InlineData("file:///C:/secret.txt")]
    [InlineData("ftp://exemple.fr/")]
    [InlineData("pas une url du tout")]
    [InlineData("")]
    public void DomainOf_rejette_ce_qui_n_est_pas_du_web(string url)
        => Assert.Null(FaviconService.DomainOf(url));

    // ---------------------------------------------------------------- ce qui part chez le tiers

    [Fact]
    public void BuildUrl_n_envoie_que_le_domaine()
    {
        var built = FaviconService.BuildUrl("admin.wiliwo.com");

        Assert.Contains("admin.wiliwo.com", built);
        Assert.Contains("sz=128", built);
    }

    /// <summary>
    /// La garantie annoncée à l'utilisateur : le chemin et les paramètres de son URL ne quittent
    /// jamais sa machine. Un identifiant de projet Asana ou un numéro de client dans une URL
    /// interne n'a rien à faire chez un tiers.
    /// </summary>
    [Fact]
    public void BuildUrl_ne_porte_ni_chemin_ni_parametres()
    {
        var domain = FaviconService.DomainOf("https://app.asana.com/1/310162866158972/project/1210388890592482");
        var built = FaviconService.BuildUrl(domain!);

        Assert.DoesNotContain("310162866158972", built);
        Assert.DoesNotContain("project", built);
    }

    // ---------------------------------------------------------------- quand on ne demande rien

    [Fact]
    public void ShouldFetch_oui_pour_une_tuile_web_sans_icone()
        => Assert.True(FaviconService.ShouldFetch(true, ShortcutType.OpenUrl, "", "https://exemple.fr"));

    [Fact]
    public void ShouldFetch_non_si_le_reglage_est_decoche()
        => Assert.False(FaviconService.ShouldFetch(false, ShortcutType.OpenUrl, "", "https://exemple.fr"));

    [Theory]
    [InlineData(ShortcutType.RunCommand)]
    [InlineData(ShortcutType.OpenFolder)]
    [InlineData(ShortcutType.OpenTerminal)]
    [InlineData(ShortcutType.SwitchToProcess)]
    public void ShouldFetch_non_pour_les_autres_types(ShortcutType type)
        => Assert.False(FaviconService.ShouldFetch(true, type, "", "https://exemple.fr"));

    /// <summary>Une icône choisie à la main gagne toujours : on ne va pas la remplacer.</summary>
    [Fact]
    public void ShouldFetch_non_si_une_icone_est_deja_fournie()
        => Assert.False(FaviconService.ShouldFetch(true, ShortcutType.OpenUrl,
                                                   @"C:\dev\Dock-icons\vscode.png", "https://exemple.fr"));

    [Fact]
    public void ShouldFetch_non_si_la_commande_n_est_pas_une_url_web()
        => Assert.False(FaviconService.ShouldFetch(true, ShortcutType.OpenUrl, "", "file:///C:/x.html"));

    // ---------------------------------------------------------------- téléchargement

    [Fact]
    public async Task TryDownloadAsync_ecrit_les_octets_recus()
    {
        byte[] png = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 1, 2, 3];
        var service = new FaviconService(new HttpClient(new StubHandler(HttpStatusCode.OK, png)));

        var path = await service.TryDownloadAsync("https://exemple.fr/page", CancellationToken.None);

        Assert.NotNull(path);
        try
        {
            Assert.Equal(png, File.ReadAllBytes(path!));
        }
        finally { File.Delete(path!); }
    }

    /// <summary>
    /// La garantie de bout en bout : ce qui part réellement sur le réseau ne porte que le domaine.
    /// Le test sur <c>BuildUrl</c> ne prouve que la construction ; celui-ci prouve l'envoi.
    /// </summary>
    [Fact]
    public async Task TryDownloadAsync_n_envoie_que_le_domaine()
    {
        var handler = new StubHandler(HttpStatusCode.OK, [1, 2, 3]);
        var service = new FaviconService(new HttpClient(handler));

        await service.TryDownloadAsync(
            "https://admin.wiliwo.com/clients/8842?token=secret", CancellationToken.None);

        Assert.NotNull(handler.RequestedUrl);
        Assert.Contains("admin.wiliwo.com", handler.RequestedUrl!);
        Assert.DoesNotContain("8842", handler.RequestedUrl!);
        Assert.DoesNotContain("secret", handler.RequestedUrl!);
        Assert.DoesNotContain("clients", handler.RequestedUrl!);
    }

    [Fact]
    public async Task TryDownloadAsync_rend_null_sur_un_echec_http()
    {
        var service = new FaviconService(new HttpClient(new StubHandler(HttpStatusCode.NotFound, [])));

        Assert.Null(await service.TryDownloadAsync("https://exemple.fr/", CancellationToken.None));
    }

    /// <summary>Une réponse vide n'est pas une icône : mieux vaut rien que zéro octet dans le store.</summary>
    [Fact]
    public async Task TryDownloadAsync_rend_null_sur_une_reponse_vide()
    {
        var service = new FaviconService(new HttpClient(new StubHandler(HttpStatusCode.OK, [])));

        Assert.Null(await service.TryDownloadAsync("https://exemple.fr/", CancellationToken.None));
    }

    /// <summary>Hors ligne, DNS cassé, proxy d'entreprise : rien ne remonte à l'utilisateur.</summary>
    [Fact]
    public async Task TryDownloadAsync_rend_null_quand_le_reseau_leve()
    {
        var service = new FaviconService(new HttpClient(new ThrowingHandler()));

        Assert.Null(await service.TryDownloadAsync("https://exemple.fr/", CancellationToken.None));
    }

    [Fact]
    public async Task TryDownloadAsync_rend_null_pour_une_url_non_web()
    {
        var service = new FaviconService(new HttpClient(new StubHandler(HttpStatusCode.OK, [1, 2, 3])));

        Assert.Null(await service.TryDownloadAsync(@"C:\dev", CancellationToken.None));
    }

    // ---------------------------------------------------------------- doublures

    private sealed class StubHandler(HttpStatusCode status, byte[] body) : HttpMessageHandler
    {
        public string? RequestedUrl { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            RequestedUrl = request.RequestUri?.ToString();
            return Task.FromResult(new HttpResponseMessage(status) { Content = new ByteArrayContent(body) });
        }
    }

    private sealed class ThrowingHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
            => throw new HttpRequestException("réseau indisponible");
    }
}
