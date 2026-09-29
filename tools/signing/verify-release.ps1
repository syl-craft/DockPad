param(
    [Parameter(Mandatory)][string]$Directory,
    [Parameter(Mandatory)][ValidatePattern('^\d+\.\d+\.\d+$')][string]$Version,
    [Parameter(Mandatory)][ValidatePattern('^[A-Fa-f0-9]{40}$')][string]$CertificateThumbprint
)
$ErrorActionPreference = 'Stop'
$Directory = [IO.Path]::GetFullPath($Directory)
function Assert-Signature([string]$Path) {
    if (!(Test-Path -LiteralPath $Path -PathType Leaf)) { throw "Missing release binary: $Path" }
    $signature = Get-AuthenticodeSignature -LiteralPath $Path
    if ($signature.Status -ne 'Valid' -or $signature.SignerCertificate.Thumbprint -ne $CertificateThumbprint -or !$signature.TimeStamperCertificate) {
        throw "Missing trusted, timestamped publisher signature: $Path ($($signature.Status))"
    }
}
Assert-Signature "$Directory/DockPad-win-Setup.exe"
Assert-Signature "$Directory/DockPad-$Version-win-x64-Setup.exe"
if ((Get-FileHash "$Directory/DockPad-win-Setup.exe").Hash -ne (Get-FileHash "$Directory/DockPad-$Version-win-x64-Setup.exe").Hash) { throw 'Setup aliases differ' }
$scratch = Join-Path ([IO.Path]::GetTempPath()) ('dockpad-signature-check-' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory $scratch | Out-Null
try {
    [IO.Compression.ZipFile]::ExtractToDirectory("$Directory/DockPad-win-Portable.zip", "$scratch/portable")
    Assert-Signature "$scratch/portable/DockPad.exe"
    Assert-Signature "$scratch/portable/Update.exe"
    & "$PSScriptRoot/verify-application.ps1" -Directory "$scratch/portable/current" -Version $Version -CertificateThumbprint $CertificateThumbprint
    [IO.Compression.ZipFile]::ExtractToDirectory("$Directory/DockPad-$Version-full.nupkg", "$scratch/package")
    & "$PSScriptRoot/verify-application.ps1" -Directory "$scratch/package/lib/app" -Version $Version -CertificateThumbprint $CertificateThumbprint
    Assert-Signature "$scratch/package/lib/app/Squirrel.exe"
    Assert-Signature "$scratch/package/lib/app/DockPad_ExecutionStub.exe"
    $feed = Get-Content "$Directory/releases.win.json" -Raw | ConvertFrom-Json
    $assets = @($feed.Assets | Where-Object Version -eq $Version)
    if (!($assets | Where-Object { $_.Type -eq 'Full' -and $_.FileName -eq "DockPad-$Version-full.nupkg" })) { throw 'Signed full package absent from feed' }
    foreach ($asset in $assets) {
        if ([IO.Path]::GetFileName($asset.FileName) -ne $asset.FileName) { throw 'Invalid asset path in feed' }
        $path = Join-Path $Directory $asset.FileName
        if ((Get-Item -LiteralPath $path).Length -ne $asset.Size -or
            (Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash -ne $asset.SHA256 -or
            (Get-FileHash -LiteralPath $path -Algorithm SHA1).Hash -ne $asset.SHA1) { throw "Feed mismatch: $($asset.FileName)" }
    }
} finally {
    # Delete only this invocation's verified direct child of TEMP.
    $resolved = [IO.Path]::GetFullPath($scratch)
    if ([IO.Path]::GetDirectoryName($resolved).TrimEnd('\') -ne [IO.Path]::GetTempPath().TrimEnd('\') -or
        [IO.Path]::GetFileName($resolved) -notmatch '^dockpad-signature-check-[0-9a-f]{32}$') { throw 'Unsafe cleanup path' }
    Remove-Item -LiteralPath $resolved -Recurse -Force
}
Write-Host 'PASS: Setup, portable launcher, updater, application, full package and feed hashes.'
