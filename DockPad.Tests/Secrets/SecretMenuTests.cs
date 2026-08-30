using DockPad.Secrets;

namespace DockPad.Tests.Secrets;

/// <summary>
/// La clé per-user de l'entrée de menu contextuel, et ce qui compte comme « installée ».
/// </summary>
public class SecretMenuTests
{
    private const string Exe = @"C:\DockPad\DockPad.exe";

    [Fact]
    public void UneSeuleCle_SurTousLesFichiers()
    {
        // Le ciblage par extension ratait les fichiers sans extension, et aussi .env.prod ou
        // .env.local — Windows lit leur extension comme « .prod » et « .local ». Aucune liste ne
        // pouvait les couvrir. C'est le motif per-user qu'emploient déjà VS Code et MobaDiff.
        Assert.Equal(@"Software\Classes\*\shell\DockPadInjectSecrets", SecretMenu.KeyPath);
    }

    [Fact]
    public void LaCommandePasseLeFichierEnArgument()
    {
        Assert.Equal($"\"{Exe}\" --inject-secrets \"%1\"", SecretMenu.BuildCommand(Exe));
    }

    [Fact]
    public void InstalleeQuandLaCleePorteLaCommandeDeLExeCourant()
    {
        Assert.True(SecretMenu.IsInstalledIn(Installed(Exe), Exe));
    }

    [Fact]
    public void PasInstalleeSiLExeABouge()
    {
        // Même règle que l'état d'enregistrement navigateur : une entrée qui pointe sur un exe
        // disparu est morte, et l'annoncer « installée » ferait chercher le problème ailleurs.
        Assert.False(SecretMenu.IsInstalledIn(Installed(@"D:\ancien\DockPad.exe"), Exe));
    }

    [Fact]
    public void PasInstalleeQuandLeRegistreEstVide()
    {
        Assert.False(SecretMenu.IsInstalledIn(_ => null, Exe));
    }

    /// <summary>Un registre où l'entrée « tous fichiers » est installée pour l'exe donné.</summary>
    private static Func<string, string?> Installed(string exe) =>
        key => key == SecretMenu.KeyPath ? SecretMenu.BuildCommand(exe) : null;
}
