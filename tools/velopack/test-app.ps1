param([string]$Revision = 'HEAD', [switch]$WorkingTree, [string]$BaseVersion)
$ErrorActionPreference = 'Stop'
$repo = [IO.Path]::GetFullPath("$PSScriptRoot/../..")
$fixture = Join-Path $env:TEMP ('dockpad-app-acceptance-' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Force "$fixture/profile" | Out-Null
git -C $repo archive $Revision -o "$fixture/source.zip"
if ($LASTEXITCODE) { throw 'Cannot snapshot app source' }
Expand-Archive "$fixture/source.zip" "$fixture/source"
if ($WorkingTree) {
    git -C $repo diff --binary $Revision --output="$fixture/working.patch"
    git -C "$fixture/source" apply "$fixture/working.patch"
    if ($LASTEXITCODE) { throw 'Cannot apply working changes to fixture' }
}
if (!$BaseVersion) { $BaseVersion = ([xml](Get-Content "$fixture/source/DockPad.csproj")).Project.PropertyGroup.Version | Where-Object { $_ } }
if ($BaseVersion -notmatch '^\d+\.\d+\.\d+$') { throw 'Expected stable base version' }
$base = [Version]$BaseVersion
$nextVersion = [Version]::new($base.Major, $base.Minor, $base.Build + 1).ToString()
@{ Base = $BaseVersion; Next = $nextVersion } | ConvertTo-Json | Set-Content "$fixture/versions.json"
# Isolate OS identities only. Application behavior, updater and hotkey handling are unchanged.
foreach ($file in @('Services/StartupRelay.cs', 'Services/UrlPipeService.cs', 'Services/McpPipeService.cs')) {
    $path = "$fixture/source/$file"
    $text = (Get-Content -LiteralPath $path -Raw).Replace('DockPad_SingleInstance','DockPad_NIN80_BenchMutex').Replace('DockPad_UrlPipe','DockPad_NIN80_BenchUrl').Replace('DockPad_ShowPipe','DockPad_NIN80_BenchShow').Replace('DockPad_InjectPipe','DockPad_NIN80_BenchInject').Replace('DockPad_McpPipe','DockPad_NIN80_BenchMcp')
    Set-Content -LiteralPath $path -Value $text -NoNewline
}
foreach ($file in @('Services/BrowserRegistrationService.cs', 'Services/SettingsService.cs', 'Secrets/SecretMenu.cs')) {
    $path = "$fixture/source/$file"
    $text = (Get-Content -LiteralPath $path -Raw).Replace('"DockPad"','"DockPadNIN80"').Replace('DockPadURL','DockPadNIN80URL').Replace('Software\DockPad\','Software\DockPadNIN80\').Replace('DockPadInjectSecrets','DockPadNIN80InjectSecrets')
    Set-Content -LiteralPath $path -Value $text -NoNewline
}
Set-Content "$fixture/profile/settings.json" '{"hotkeyModifiers":6,"hotkeyKey":135,"checkForUpdates":false,"autoFavicon":false}'
Set-Content "$fixture/profile/mcp.json" '{"enabled":true,"allowDelete":false}'
Set-Content "$fixture/profile/sentinel.json" '{"keep":"NIN-80"}'
Set-Content "$fixture/fixture.env" 'TOKEN={{vault.test.password}}'
foreach ($version in @($BaseVersion, $nextVersion)) {
    dotnet publish "$fixture/source/DockPad.csproj" -c Release -r win-x64 --self-contained false -p:SkipLegacyZip=true -p:UseSharedCompilation=false -m:1 "-p:Version=$version" -o "$fixture/stage/$version"
    if ($LASTEXITCODE) { throw 'Actual app build failed' }
    & "$repo/.tools/vpk/vpk.exe" pack --packId DockPad --packTitle DockPad --packAuthors syl-craft --packVersion $version --packDir "$fixture/stage/$version" --mainExe DockPad.exe --runtime win-x64 --framework net8-x64-desktop --outputDir "$fixture/feed" --shortcuts None --yes
    if ($LASTEXITCODE) { throw 'Actual app packaging failed' }
    if ($version -eq $BaseVersion) { Expand-Archive "$fixture/feed/DockPad-win-Portable.zip" "$fixture/app" }
}
foreach ($project in @('AppAcceptance', 'AppAcceptanceHook')) {
    dotnet build "$repo/tools/$project" -c Release -p:UseSharedCompilation=false -m:1 -v minimal
    if ($LASTEXITCODE) { throw "Acceptance tool build failed: $project" }
}
$keys = @('Software\DockPadNIN80', 'Software\Classes\DockPadNIN80URL', 'Software\Classes\*\shell\DockPadNIN80InjectSecrets')
foreach ($path in $keys) {
    $existing = [Microsoft.Win32.Registry]::CurrentUser.OpenSubKey($path)
    if ($null -ne $existing) { $existing.Dispose(); throw "Refusing to overwrite existing fixture key: $path" }
}
$run = [Microsoft.Win32.Registry]::CurrentUser.CreateSubKey('Software\Microsoft\Windows\CurrentVersion\Run')
if ($null -ne $run.GetValue('DockPadNIN80')) { $run.Dispose(); throw 'Fixture auto-start entry already exists' }
try {
    [Microsoft.Win32.Registry]::CurrentUser.CreateSubKey('Software\DockPadNIN80\Capabilities').Dispose()
    $injection = [Microsoft.Win32.Registry]::CurrentUser.CreateSubKey('Software\Classes\*\shell\DockPadNIN80InjectSecrets\command')
    $injection.SetValue('', 'fixture'); $injection.Dispose()
    $run.SetValue('DockPadNIN80', 'fixture')
    & "$repo/tools/AppAcceptance/bin/Release/net8.0-windows/AppAcceptance.exe" $fixture "$repo/tools/AppAcceptanceHook/bin/Release/net8.0-windows/AppAcceptanceHook.dll"
    if ($LASTEXITCODE) { throw "App acceptance failed; evidence: $fixture" }
} finally {
    $run.DeleteValue('DockPadNIN80', $false); $run.Dispose()
    foreach ($path in $keys) { [Microsoft.Win32.Registry]::CurrentUser.DeleteSubKeyTree($path, $false) }
    $apps = [Microsoft.Win32.Registry]::CurrentUser.OpenSubKey('Software\RegisteredApplications', $true)
    if ($null -ne $apps) { $apps.DeleteValue('DockPadNIN80', $false); $apps.Dispose() }
}
Write-Host "Acceptance evidence: $fixture"
