namespace DockPad.Services.Updates;

public enum UpdateAction { None, Check, Close, Stop, Install, DownloadLatest }

/// <summary>Décisions d'affichage sans WPF : une action garde le même sens dans chaque état.</summary>
public sealed record UpdatePresentation(string TitleKey, string LabelKey,
    UpdateAction Secondary, string SecondaryKey, UpdateAction Primary, string PrimaryKey,
    bool PrimaryEnabled = true, bool PrimaryAccent = true)
{
    public static UpdatePresentation For(UpdateState state, bool hasChecked) => state switch
    {
        UpdateState.Unavailable => new("Update_TitleUnavailable", "Update_StateUnavailable",
            UpdateAction.DownloadLatest, "Update_DownloadLatest", UpdateAction.Close, "Common_Close", PrimaryAccent: false),
        UpdateState.Idle => new(hasChecked ? "Update_TitleCurrent" : "Update_Title", hasChecked ? "Update_StateCurrent" : "Update_StateIdle",
            UpdateAction.Check, "Update_Check", UpdateAction.Close, "Common_Close", PrimaryAccent: false),
        UpdateState.Checking => new("Update_TitleChecking", "Update_StateChecking",
            UpdateAction.Close, "Common_Close", UpdateAction.Check, "Update_Check", PrimaryEnabled: false),
        UpdateState.Available => new("Update_TitleAvailable", "Update_StateAvailable",
            UpdateAction.Close, "Update_Later", UpdateAction.Install, "Update_DownloadInstall"),
        UpdateState.Downloading => new("Update_TitleDownloading", "Update_StateDownloading",
            UpdateAction.Stop, "Update_Stop", UpdateAction.Install, "Update_RestartInstall", PrimaryEnabled: false),
        UpdateState.Ready => new("Update_TitleReady", "Update_StateReady",
            UpdateAction.Close, "Update_Later", UpdateAction.Install, "Update_RestartInstall"),
        UpdateState.Failed => new("Update_TitleFailed", "Update_StateFailed",
            UpdateAction.Close, "Common_Close", UpdateAction.Check, "Update_Retry"),
        UpdateState.Preparing => new("Update_TitlePreparing", "Update_StatePreparing",
            UpdateAction.None, "Update_Later", UpdateAction.Install, "Update_RestartInstall", PrimaryEnabled: false),
        UpdateState.Installing => new("Update_TitleInstalling", "Update_StateInstalling",
            UpdateAction.None, "Update_Later", UpdateAction.Install, "Update_RestartInstall", PrimaryEnabled: false),
        _ => throw new ArgumentOutOfRangeException(nameof(state)),
    };
}
