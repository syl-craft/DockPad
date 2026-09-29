param(
    [Parameter(Mandatory)][string]$Directory,
    [Parameter(Mandatory)][ValidatePattern('^\d+\.\d+\.\d+$')][string]$Version,
    [Parameter(Mandatory)][ValidatePattern('^[A-Fa-f0-9]{40}$')][string]$CertificateThumbprint
)
$ErrorActionPreference = 'Stop'
$files = @('DockPad.exe', 'DockPad.dll', 'DockPad.Core.dll', 'fr/DockPad.Core.resources.dll', 'qps-Ploc/DockPad.Core.resources.dll')
foreach ($relative in $files) {
    $path = Join-Path $Directory $relative
    if (!(Test-Path -LiteralPath $path -PathType Leaf)) { throw "Signed application file missing: $relative" }
    $signature = Get-AuthenticodeSignature -LiteralPath $path
    if ($signature.Status -ne 'Valid' -or $signature.SignerCertificate.Thumbprint -ne $CertificateThumbprint -or !$signature.TimeStamperCertificate) {
        throw "Expected trusted, timestamped publisher signature missing: $relative ($($signature.Status))"
    }
    $metadata = [Diagnostics.FileVersionInfo]::GetVersionInfo([IO.Path]::GetFullPath($path))
    if ($metadata.ProductName -ne 'DockPad' -or $metadata.ProductVersion -ne $Version) { throw "Unexpected signed metadata: $relative" }
}
Write-Host "PASS: five DockPad binaries, publisher certificate, timestamps and version $Version."
