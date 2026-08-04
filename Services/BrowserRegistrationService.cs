using Microsoft.Win32;

namespace DockPad.Services;

public enum BrowserRegistrationStatus
{
    NotRegistered, // clés absentes ou chemin d'exe obsolète
    Registered,    // enregistré mais pas navigateur par défaut
    Default,       // navigateur par défaut (http)
}

/// <summary>
/// Enregistre DockPad comme navigateur per-user (HKCU, sans admin).
/// Windows 10/11 ne permet pas de définir le défaut par programme (hash UserChoice) :
/// l'utilisateur choisit DockPad dans ms-settings:defaultapps.
/// </summary>
public static class BrowserRegistrationService
{
    private const string ProgId           = "DockPadURL";
    private const string CapabilitiesPath = @"Software\DockPad\Capabilities";
    private const string UserChoicePath   = @"Software\Microsoft\Windows\Shell\Associations\UrlAssociations\http\UserChoice";

    private static string ExePath =>
        Environment.ProcessPath ?? System.Diagnostics.Process.GetCurrentProcess().MainModule!.FileName;

    private static string OpenCommand => $"\"{ExePath}\" --url \"%1\"";

    public static void Register()
    {
        using (var cap = Registry.CurrentUser.CreateSubKey(CapabilitiesPath))
        {
            cap.SetValue("ApplicationName", "DockPad");
            cap.SetValue("ApplicationDescription", "Sélecteur de navigateur DockPad");
            using var assoc = cap.CreateSubKey("URLAssociations");
            assoc.SetValue("http",  ProgId);
            assoc.SetValue("https", ProgId);
        }

        using (var reg = Registry.CurrentUser.CreateSubKey(@"Software\RegisteredApplications"))
            reg.SetValue("DockPad", CapabilitiesPath);

        using var progId = Registry.CurrentUser.CreateSubKey($@"Software\Classes\{ProgId}");
        progId.SetValue(null, "URL DockPad");
        using (var icon = progId.CreateSubKey("DefaultIcon"))
            icon.SetValue(null, $"{ExePath},0");
        using (var cmd = progId.CreateSubKey(@"shell\open\command"))
            cmd.SetValue(null, OpenCommand);
    }

    public static BrowserRegistrationStatus GetStatus()
    {
        // Le chemin d'exe fait partie de la commande : si l'app a été déplacée,
        // la comparaison échoue et l'état repasse à NotRegistered (Register() répare).
        using var cmd = Registry.CurrentUser.OpenSubKey($@"Software\Classes\{ProgId}\shell\open\command");
        if (cmd?.GetValue(null) as string != OpenCommand)
            return BrowserRegistrationStatus.NotRegistered;

        using var choice = Registry.CurrentUser.OpenSubKey(UserChoicePath);
        return choice?.GetValue("ProgId") as string == ProgId
            ? BrowserRegistrationStatus.Default
            : BrowserRegistrationStatus.Registered;
    }

    public static void OpenDefaultAppsSettings() =>
        System.Diagnostics.Process.Start(
            new System.Diagnostics.ProcessStartInfo("ms-settings:defaultapps") { UseShellExecute = true });
}
