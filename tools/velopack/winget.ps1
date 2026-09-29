param(
    [Parameter(Mandatory)][string]$Version,
    [Parameter(Mandatory)][string]$Installer,
    [Parameter(Mandatory)][string]$License,
    [Parameter(Mandatory)][uri]$LicenseUrl,
    [string]$Output = "$PSScriptRoot/../../release/winget"
)
$ErrorActionPreference = 'Stop'
if ($Version -notmatch '^\d+\.\d+\.\d+$') { throw 'A stable x.y.z version is required' }
if ($License -notmatch '^[\w.+-]+$' -or $LicenseUrl.Scheme -ne 'https') { throw 'Provide a license identifier and HTTPS license URL (NIN-6)' }
if (!(Test-Path -LiteralPath $Installer)) { throw 'Installer missing' }
# Generate only from the FINAL signed installer: signing changes SHA256.
if ((Get-AuthenticodeSignature -LiteralPath $Installer).Status -ne 'Valid') { throw 'A valid signed installer is required before generating the WinGet manifest (NIN-9)' }
$hash = (Get-FileHash -LiteralPath $Installer -Algorithm SHA256).Hash
$dest = Join-Path ([IO.Path]::GetFullPath($Output)) "s/syl-craft/DockPad/$Version"
New-Item -ItemType Directory -Force $dest | Out-Null
@"
# yaml-language-server: `$schema=https://aka.ms/winget-manifest.defaultLocale.1.10.0.schema.json
PackageIdentifier: syl-craft.DockPad
PackageVersion: $Version
PackageLocale: fr-FR
Publisher: syl-craft
PublisherUrl: https://github.com/syl-craft
PackageName: DockPad
PackageUrl: https://github.com/syl-craft/DockPad
License: $License
LicenseUrl: $LicenseUrl
ShortDescription: Lanceur de raccourcis et routeur de liens pour Windows.
ManifestType: defaultLocale
ManifestVersion: 1.10.0
"@ | Set-Content -LiteralPath "$dest/syl-craft.DockPad.locale.fr-FR.yaml" -Encoding utf8
@"
# yaml-language-server: `$schema=https://aka.ms/winget-manifest.installer.1.10.0.schema.json
PackageIdentifier: syl-craft.DockPad
PackageVersion: $Version
InstallerType: exe
Scope: user
InstallModes:
  - interactive
  - silent
InstallerSwitches:
  Silent: --silent
  SilentWithProgress: --silent
UpgradeBehavior: install
AppsAndFeaturesEntries:
  - DisplayName: DockPad
    Publisher: syl-craft
    DisplayVersion: $Version
    ProductCode: DockPad
Installers:
  - Architecture: x64
    InstallerUrl: https://github.com/syl-craft/DockPad/releases/download/v$Version/DockPad-$Version-win-x64-Setup.exe
    InstallerSha256: $hash
ManifestType: installer
ManifestVersion: 1.10.0
"@ | Set-Content -LiteralPath "$dest/syl-craft.DockPad.installer.yaml" -Encoding utf8
@"
# yaml-language-server: `$schema=https://aka.ms/winget-manifest.version.1.10.0.schema.json
PackageIdentifier: syl-craft.DockPad
PackageVersion: $Version
DefaultLocale: fr-FR
ManifestType: version
ManifestVersion: 1.10.0
"@ | Set-Content -LiteralPath "$dest/syl-craft.DockPad.yaml" -Encoding utf8
Write-Host "Manifest ready: $dest. Validate with winget validate --manifest before submitting NIN-8."
