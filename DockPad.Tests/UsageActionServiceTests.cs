using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using DockPad.Models;
using DockPad.Services;
using DockPad.Services.Usage;
using Xunit;

namespace DockPad.Tests;

/// <summary>
/// Quotas exposés au serveur MCP par dockpad_usage_get : fenêtres de session et de semaine,
/// relevé ancien, quota refusé, fournisseur sans quota, masquage du bandeau, fournisseur nommé
/// (masqué, inconnu, absent du poste) et exclusion du fournisseur de démonstration.
/// </summary>
public class UsageActionServiceTests
{
    private static readonly DateTime Now = new(2026, 10, 1, 14, 0, 0, DateTimeKind.Local);

    private static readonly JsonSerializerOptions JsonOpts = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    private static JsonElement DataOf(ActionResult result)
    {
        Assert.True(result.Ok, result.Error);
        return JsonDocument.Parse(JsonSerializer.Serialize(result.Data, JsonOpts)).RootElement;
    }

    private static JsonElement[] ProvidersOf(ActionResult result) =>
        DataOf(result).GetProperty("providers").EnumerateArray().ToArray();

    private sealed class FakeProvider(string id, AiUsage? usage) : IUsageProvider
    {
        public string Id => id;
        public string Name => id;
        public AiProbe Probe() => new() { Available = usage is not null, DisplayName = id, Glyph = "?", AccentColor = "#000000" };
        public Task<AiUsage?> ReadAsync(CancellationToken ct) => Task.FromResult(usage);
    }

    private static AiUsage Usage(string id, UsageWindow? session = null, UsageWindow? week = null,
                                 string notice = "", string noticeNote = "", bool isDemo = false) => new()
    {
        ProviderId = id, Name = id, Glyph = "?", AccentColor = "#000000",
        Session = session, Week = week, QuotaNotice = notice, QuotaNoticeNote = noticeNote, IsDemo = isDemo,
    };

    private static ActionResult Get(string? provider, UsageConfig? config, params IUsageProvider[] providers) =>
        UsageActionService.GetQuotas(provider, new UsageService(providers), config ?? new UsageConfig(), Now);

    [Fact]
    public void FenetresDeQuota_ConsommeRestantEtRemiseAZero()
    {
        var claude = new FakeProvider("claude", Usage("claude",
            session: new UsageWindow { UsedPct = 62, ResetsAt = Now.AddMinutes(94) },
            week: new UsageWindow { UsedPct = 44, ResetsAt = Now.AddDays(4) }));

        var entry = Assert.Single(ProvidersOf(Get(null, null, claude)));
        var session = entry.GetProperty("session");

        Assert.Equal("claude", entry.GetProperty("id").GetString());
        Assert.True(entry.GetProperty("quotaAvailable").GetBoolean());
        Assert.Equal(62, session.GetProperty("usedPct").GetInt32());
        Assert.Equal(38, session.GetProperty("remainingPct").GetInt32());
        Assert.Equal(94, session.GetProperty("minutesUntilReset").GetInt32());
        Assert.Equal(new DateTimeOffset(Now.AddMinutes(94)), DateTimeOffset.Parse(session.GetProperty("resetsAt").GetString()!));
        Assert.False(session.GetProperty("stale").GetBoolean());
        Assert.Equal(4 * 24 * 60, entry.GetProperty("week").GetProperty("minutesUntilReset").GetInt32());
    }

    [Fact]
    public void RemiseAZeroDejaPassee_ZeroMinuteJamaisNegatif()
    {
        var codex = new FakeProvider("codex", Usage("codex", week: new UsageWindow { UsedPct = 10, ResetsAt = Now.AddMinutes(-3) }));

        var week = Assert.Single(ProvidersOf(Get(null, null, codex))).GetProperty("week");

        Assert.Equal(0, week.GetProperty("minutesUntilReset").GetInt32());
    }

    [Fact]
    public void ReleveAncien_SignaleCommeTelAvecSonHeure()
    {
        var observed = Now.AddHours(-1);
        var codex = new FakeProvider("codex", Usage("codex",
            week: new UsageWindow { UsedPct = 27, ResetsAt = Now.AddDays(2), ObservedAt = observed }));

        var week = Assert.Single(ProvidersOf(Get(null, null, codex))).GetProperty("week");

        Assert.True(week.GetProperty("stale").GetBoolean());
        Assert.Equal(new DateTimeOffset(observed), DateTimeOffset.Parse(week.GetProperty("observedAt").GetString()!));
    }

    [Fact]
    public void FenetreAbsente_NullPlutotQueZero()
    {
        var codex = new FakeProvider("codex", Usage("codex", week: new UsageWindow { UsedPct = 27 }));

        var entry = Assert.Single(ProvidersOf(Get(null, null, codex)));

        Assert.Equal(JsonValueKind.Null, entry.GetProperty("session").ValueKind);
        Assert.Equal(JsonValueKind.Null, entry.GetProperty("week").GetProperty("resetsAt").ValueKind);
        Assert.Equal(JsonValueKind.Null, entry.GetProperty("week").GetProperty("minutesUntilReset").ValueKind);
    }

    [Fact]
    public void QuotaRefuse_IndisponibleAvecSaNotice()
    {
        var claude = new FakeProvider("claude", Usage("claude", notice: "Quota indisponible — nouvelle tentative dans 4 min",
                                                       noticeNote: "HTTP 429 TooManyRequests"));

        var entry = Assert.Single(ProvidersOf(Get(null, null, claude)));

        Assert.False(entry.GetProperty("quotaAvailable").GetBoolean());
        Assert.Equal("Quota indisponible — nouvelle tentative dans 4 min", entry.GetProperty("notice").GetString());
        Assert.Equal("HTTP 429 TooManyRequests", entry.GetProperty("noticeDetail").GetString());
    }

    [Fact]
    public void SansQuotaParNature_AbsentDeLaListe()
    {
        var claude = new FakeProvider("claude", Usage("claude", session: new UsageWindow { UsedPct = 5 }));
        var gemini = new FakeProvider("gemini", Usage("gemini"));

        var ids = ProvidersOf(Get(null, null, claude, gemini)).Select(p => p.GetProperty("id").GetString());

        Assert.Equal(["claude"], ids);
    }

    [Fact]
    public void SansFournisseurNomme_MasqueDuBandeauEtDemoExclus()
    {
        var claude = new FakeProvider("claude", Usage("claude", session: new UsageWindow { UsedPct = 5 }));
        var codex = new FakeProvider("codex", Usage("codex", week: new UsageWindow { UsedPct = 9 }));
        var demo = new FakeProvider("demo", Usage("demo", session: new UsageWindow { UsedPct = 62 }, isDemo: true));
        var config = new UsageConfig { Providers = [new AiProviderEntry { Id = "codex", Hidden = true }] };

        var ids = ProvidersOf(Get(null, config, claude, codex, demo)).Select(p => p.GetProperty("id").GetString());

        Assert.Equal(["claude"], ids);
    }

    [Fact]
    public void FournisseurNomme_LuMemeMasque()
    {
        var codex = new FakeProvider("codex", Usage("codex", week: new UsageWindow { UsedPct = 9 }));
        var config = new UsageConfig { Providers = [new AiProviderEntry { Id = "codex", Hidden = true }] };

        var entry = Assert.Single(ProvidersOf(Get("Codex", config, codex)));

        Assert.Equal("codex", entry.GetProperty("id").GetString());
    }

    [Fact]
    public void FournisseurNomme_DemoLuSurDemande()
    {
        var demo = new FakeProvider("demo", Usage("demo", session: new UsageWindow { UsedPct = 62 }, isDemo: true));

        var entry = Assert.Single(ProvidersOf(Get("demo", null, demo)));

        Assert.True(entry.GetProperty("isDemo").GetBoolean());
    }

    [Fact]
    public void FournisseurNommeSansQuota_PresentEtExpliquePourquoi()
    {
        var gemini = new FakeProvider("gemini", Usage("gemini"));

        var entry = Assert.Single(ProvidersOf(Get("gemini", null, gemini)));

        Assert.False(entry.GetProperty("quotaAvailable").GetBoolean());
        Assert.Contains("quota", entry.GetProperty("notice").GetString());
    }

    [Fact]
    public void FournisseurNommeInconnu_RefusQuiListeLesIds()
    {
        var result = Get("chatgpt", null, new FakeProvider("claude", null), new FakeProvider("codex", null));

        Assert.False(result.Ok);
        Assert.Contains("chatgpt", result.Error);
        Assert.Contains("claude", result.Error);
        Assert.Contains("codex", result.Error);
    }

    [Fact]
    public void FournisseurNommeAbsentDuPoste_RefusExplicite()
    {
        var result = Get("copilot", null, new FakeProvider("copilot", null));

        Assert.False(result.Ok);
        Assert.Contains("copilot", result.Error);
    }

    [Fact]
    public void Dispatcher_LectureSeule_NiRefuseeParAllowDeleteNiMutation()
    {
        var mutations = 0;
        var previousService = UsageActionService.Service;
        var previousMutation = McpDispatcher.OnMutation;
        UsageActionService.Service = new UsageService([new FakeProvider("demo",
            Usage("demo", session: new UsageWindow { UsedPct = 62 }, isDemo: true))]);
        McpDispatcher.OnMutation = () => mutations++;
        try
        {
            var response = JsonDocument.Parse(McpDispatcher.Handle(
                """{"tool":"dockpad_usage_get","args":{"provider":"demo"}}""",
                new McpConfig { Enabled = true, AllowDelete = false })).RootElement;

            Assert.True(response.GetProperty("ok").GetBoolean(), response.GetProperty("error").GetString());
            Assert.Equal(0, mutations);
        }
        finally
        {
            UsageActionService.Service = previousService;
            McpDispatcher.OnMutation = previousMutation;
        }
    }
}
