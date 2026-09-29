param(
    [string]$Version,
    [string]$Output = "$PSScriptRoot/../../release/velopack",
    [string]$Native = "$PSScriptRoot/../../.tools/velopack-native",
    [string]$ReleaseNotes,
    [string]$SignTemplate,
    [switch]$RequireSigned,
    [string]$CertificateThumbprint
)
$ErrorActionPreference = 'Stop'
if ($RequireSigned -and (!$SignTemplate -or $CertificateThumbprint -notmatch '^[A-Fa-f0-9]{40}$')) {
    throw 'Signed release requires a signing command and the expected publisher certificate thumbprint.'
}
$repo = [IO.Path]::GetFullPath("$PSScriptRoot/../..")
if (!$Version) { $Version = ([xml](Get-Content "$repo/DockPad.csproj")).Project.PropertyGroup.Version | Where-Object { $_ } }
if ($Version -notmatch '^\d+\.\d+\.\d+$') { throw 'A stable x.y.z version is required' }
$Output = [IO.Path]::GetFullPath($Output)
$Native = [IO.Path]::GetFullPath($Native)
$vpkRoot = "$repo/.tools/vpk"
if (!(Test-Path "$vpkRoot/vpk.exe")) {
    dotnet tool install vpk --version 1.2.161 --tool-path $vpkRoot
    if ($LASTEXITCODE) { throw 'Cannot install the pinned vpk tool' }
}
$vendor = "$vpkRoot/.store/vpk/1.2.161/vpk/1.2.161/vendor"
if (!(Test-Path $vendor)) { throw 'Unexpected vpk version; expected 1.2.161' }
if ((Get-Content "$Native/source-revision.txt" -Raw).Trim() -ne '92d6a1c91716729d449034df5c50307dcce39493') { throw 'Native revision does not match SDK' }
foreach ($name in @('update', 'setup', 'stub')) {
    if (!(Test-Path "$Native/${name}_x64.exe")) { throw "Build the adapted native binaries first: $name" }
    Copy-Item -LiteralPath "$Native/${name}_x64.exe" -Destination "$vendor/${name}_x64.exe"
}
# Unique staging directory: no stale binaries or profile files from a previous publish.
$stage = Join-Path $repo ('.tools/staging-' + [Guid]::NewGuid().ToString('N'))
dotnet publish "$repo/DockPad.csproj" -c Release -r win-x64 --self-contained false -p:SkipLegacyZip=true -p:UseSharedCompilation=false -m:1 -p:IncludeSourceRevisionInInformationalVersion=false "-p:Version=$Version" -o $stage
if ($LASTEXITCODE) { throw 'Publish failed' }
Copy-Item -LiteralPath "$Native/Velopack.LICENSE.txt" -Destination $stage
if (!$ReleaseNotes) {
    $changelog = Get-Content -LiteralPath "$repo/CHANGELOG.md" -Raw
    $section = [regex]::Match($changelog, '(?ms)^## \[' + [regex]::Escape($Version) + '\].*?(?=^## \[|\z)')
    if (!$section.Success) { throw 'No changelog section for this version; provide -ReleaseNotes explicitly' }
    $ReleaseNotes = "$stage/RELEASE_NOTES.md"
    Set-Content -LiteralPath $ReleaseNotes -Value $section.Value.Trim() -Encoding utf8
}
$arguments = @('pack', '--packId', 'DockPad', '--packTitle', 'DockPad', '--packAuthors', 'syl-craft',
    '--packVersion', $Version, '--packDir', $stage, '--mainExe', 'DockPad.exe', '--runtime', 'win-x64',
    '--framework', 'net8-x64-desktop', '--icon', "$repo/app.ico", '--outputDir', $Output,
    '--releaseNotes', [IO.Path]::GetFullPath($ReleaseNotes), '--shortcuts', 'StartMenuRoot', '--instLocation', 'PerUser')
if ($SignTemplate) { $arguments += @('--signTemplate', $SignTemplate) }
& "$vpkRoot/vpk.exe" @arguments --yes
if ($LASTEXITCODE) { throw 'Velopack packaging failed' }
# WinGet URLs must be immutable. Retain the conventional name too for Velopack feeds.
Copy-Item -LiteralPath "$Output/DockPad-win-Setup.exe" -Destination "$Output/DockPad-$Version-win-x64-Setup.exe"
if ($RequireSigned) {
    & "$PSScriptRoot/../signing/verify-release.ps1" -Directory $Output -Version $Version -CertificateThumbprint $CertificateThumbprint
}
Write-Host "Packages ready in $Output. Signing configured: $([bool]$SignTemplate). Nothing uploaded."
