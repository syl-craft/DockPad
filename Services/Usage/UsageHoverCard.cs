using DockPad.Models;

namespace DockPad.Services.Usage;

public sealed record UsageHoverCard
{
    public required string ProviderId { get; init; }
    public required string Glyph { get; init; }
    public required string Header { get; init; }
    public required string Color { get; init; }
    public string PercentText { get; init; } = "";
    public string RemainingText { get; init; } = "";
    public double Fraction { get; init; }
    public bool HasQuota { get; init; }
    public string ResetText { get; init; } = "";
    public string ResetRelative { get; init; } = "";
    public bool HasReset => ResetText.Length > 0;
    public string Note { get; init; } = "";
    public bool HasNote => Note.Length > 0;
    public bool IsStale { get; init; }
    public bool IsAlert { get; init; }
    public string? OtherWindow { get; init; }
    public bool HasOtherWindow => OtherWindow != null;
    public string NoticeTitle { get; init; } = "";
    public string NoticeDetail { get; init; } = "";
    public bool HasNotice => NoticeTitle.Length > 0;
    public string UsageUrl { get; init; } = "";
    public bool HasUsageUrl => UsageUrl.Length > 0;
    public string AccessibleText { get; init; } = "";

    public static UsageHoverCard Create(AiUsage usage, UsageWindow? window, int alertThreshold, DateTime now,
        string? gaugeColor = null)
    {
        var isStale = window?.ObservedAt != null;
        var isAlert = window != null && isStale == false && UsageFormat.IsCritical(window.UsedPct, alertThreshold);
        var label = window == null ? "" : LabelOf(usage, window);
        var header = window == null ? usage.Name : Loc.F("UsageBar_Window_Tooltip", usage.Name, label);
        var other = window == null ? null : ReferenceEquals(window, usage.Session) ? usage.Week : usage.Session;
        var reset = UsageFormat.Reset(window?.ResetsAt, now);
        var note = window?.ObservedAt is { } observed
            ? Loc.F("UsageHover_Stale", observed)
            : isAlert ? Loc.T("UsageHover_Alert") : "";
        var noticeTitle = window == null ? usage.QuotaNotice.Split(" — ", 2)[0].Trim() : "";
        var noticeDetail = window == null ? NoticeTooltipOf(usage) : "";
        var quotaText = window == null ? "" : Loc.F("Usage_Gauge_Tooltip", window.UsedPct, window.RemainingPct);
        var resetText = reset.Length > 0 ? Loc.F("Usage_Reset_Tooltip", reset) : "";
        var relative = window?.ResetsAt is { } resetsAt ? RelativeReset(resetsAt, now) : "";
        var otherWindow = other == null ? null : Loc.F(
            other.ResetsAt == null ? "UsageHover_Other" : "UsageHover_OtherReset",
            LabelOf(usage, other), other.UsedPct, UsageFormat.Reset(other.ResetsAt, now));

        return new UsageHoverCard
        {
            ProviderId = usage.ProviderId, Glyph = usage.Glyph, Header = header,
            Color = isStale ? UsageBarViewModel.StaleColor : isAlert ? UsageFormat.Critical : gaugeColor ?? usage.AccentColor,
            HasQuota = window != null, Fraction = (window?.UsedPct ?? 0) / 100d,
            PercentText = window == null ? "" : Loc.F("UsageBar_Percent", window.UsedPct),
            RemainingText = window == null ? "" : Loc.F("UsageHover_Remaining", window.RemainingPct),
            ResetText = resetText, ResetRelative = relative,
            Note = note, IsStale = isStale, IsAlert = isAlert, OtherWindow = otherWindow,
            NoticeTitle = noticeTitle, NoticeDetail = noticeDetail,
            UsageUrl = UsagePageLink.IsWebUrl(usage.UsageUrl, out _) ? usage.UsageUrl : "",
            AccessibleText = string.Join("\n", new[] { header, quotaText, resetText, relative, note, otherWindow, noticeDetail }
                .Where(line => string.IsNullOrEmpty(line) == false)),
        };
    }

    public static string RelativeReset(DateTime resetsAt, DateTime now)
    {
        var remaining = resetsAt - now;
        if (remaining < TimeSpan.Zero) remaining = TimeSpan.Zero;
        if (remaining.TotalHours < 1) return Loc.F("UsageHover_InMinutes", (int)remaining.TotalMinutes);
        if (remaining.TotalDays < 1) return Loc.F("UsageHover_InHours", (int)remaining.TotalHours, remaining.Minutes);
        return Loc.F("UsageHover_InDays", (int)remaining.TotalDays, remaining.Hours);
    }

    public static string NoticeTooltipOf(AiUsage usage)
    {
        var notice = Loc.F("UsageBar_Logo_Notice", usage.Name, usage.QuotaNotice);
        return usage.QuotaNoticeNote.Length > 0 ? notice + "\n" + usage.QuotaNoticeNote : notice;
    }

    private static string LabelOf(AiUsage usage, UsageWindow window) => window.Label.Length > 0
        ? window.Label
        : Loc.T(ReferenceEquals(window, usage.Session) ? "UsageHover_Session" : "Usage_Gauge_Week");
}
