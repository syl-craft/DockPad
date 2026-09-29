param([string]$Native = "$PSScriptRoot/../../.tools/velopack-native")
$ErrorActionPreference = 'Stop'
$repo = [IO.Path]::GetFullPath("$PSScriptRoot/../..")
$vpk = "$repo/.tools/vpk/vpk.exe"
$vendor = "$repo/.tools/vpk/.store/vpk/1.2.161/vpk/1.2.161/vendor"
foreach ($name in @('update', 'setup', 'stub')) { Copy-Item "$Native/${name}_x64.exe" "$vendor/${name}_x64.exe" }
$testRoot = Join-Path $env:TEMP ('dockpad-update-test-' + [Guid]::NewGuid().ToString('N'))
$feed = "$testRoot/feed"
$env:DOCKPAD_UPDATE_PROBE = "$testRoot/results"
New-Item -ItemType Directory -Force $feed,$env:DOCKPAD_UPDATE_PROBE | Out-Null
function Wait-For([scriptblock]$Condition, [string]$Message) {
    $until = [DateTime]::UtcNow.AddSeconds(45)
    do { if (& $Condition) { return }; Start-Sleep -Milliseconds 100 } while ([DateTime]::UtcNow -lt $until)
    throw "Timeout: $Message ($testRoot)"
}
foreach ($version in @('1.0.0', '1.0.1')) {
    dotnet publish "$repo/tools/UpdateProbe" -c Release -r win-x64 --self-contained false "-p:Version=$version" -o "$testRoot/stage/$version"
    if ($LASTEXITCODE) { throw 'Probe build failed' }
    & $vpk pack --packId DockPad.UpdateProbe --packTitle DockPad.UpdateProbe --packAuthors syl-craft --packVersion $version --packDir "$testRoot/stage/$version" --mainExe DockPad.UpdateProbe.exe --runtime win-x64 --outputDir $feed --shortcuts None --yes
    if ($LASTEXITCODE) { throw 'Probe package failed' }
    if ($version -eq '1.0.0') {
        Copy-Item "$feed/DockPad.UpdateProbe-win-Portable.zip" "$testRoot/v1.zip"
        Copy-Item "$feed/DockPad.UpdateProbe-win-Setup.exe" "$testRoot/v1-Setup.exe"
    }
}
Expand-Archive "$testRoot/v1.zip" "$testRoot/app"
$exe = "$testRoot/app/DockPad.UpdateProbe.exe"
Set-Content "$testRoot/results/profile.json" '{"keep":"unchanged"}'
$before = (Get-FileHash "$testRoot/results/profile.json").Hash
# The root launcher keeps URL arguments in memory while an update is pending.
Set-Content "$testRoot/app/.dockpad-update" ''
$url = Start-Process -FilePath $exe -ArgumentList '--url','https://example.test/update-probe' -WindowStyle Hidden -PassThru
Start-Sleep -Milliseconds 300
if (Test-Path "$testRoot/results/url") { throw 'Launcher ignored update marker' }
$update = Start-Process -FilePath "$testRoot/app/current/DockPad.UpdateProbe.exe" -ArgumentList '--update',"`"$feed`"" -WindowStyle Hidden -PassThru
Wait-For { (Test-Path "$testRoot/results/version") -and (Get-Content "$testRoot/results/version" -Raw).Trim() -eq '1.0.1' } 'N to N+1 restart'
Wait-For { Test-Path "$testRoot/results/url" } 'URL handoff'
if ((Get-Content "$testRoot/results/url" -Raw) -ne 'https://example.test/update-probe') { throw 'URL changed' }
if ((Get-FileHash "$testRoot/results/profile.json").Hash -ne $before) { throw 'Profile changed' }
# MCP process: inherited stdio must work and the native updater must refuse to kill it.
Expand-Archive "$testRoot/v1.zip" "$testRoot/locked"
$start = [Diagnostics.ProcessStartInfo]::new("$testRoot/locked/DockPad.UpdateProbe.exe")
$start.UseShellExecute = $false; $start.CreateNoWindow = $true
$start.RedirectStandardInput = $true; $start.RedirectStandardOutput = $true
$start.ArgumentList.Add('--mcp')
$mcp = [Diagnostics.Process]::Start($start)
try {
    $ready = $mcp.StandardOutput.ReadLineAsync()
    if (!$ready.Wait(10000) -or $ready.Result -ne 'ready') { throw 'MCP stdio did not survive the stable launcher' }
    $childPid = [int](Get-Content "$testRoot/results/holding" -Raw)
    $apply = Start-Process -FilePath "$testRoot/locked/Update.exe" -ArgumentList '--silent','--log',"`"$testRoot/mcp-lock.log`"",'apply','--norestart','--package',"`"$feed/DockPad.UpdateProbe-1.0.1-full.nupkg`"" -WindowStyle Hidden -PassThru
    if (!$apply.WaitForExit(45000)) { throw 'Blocked apply timed out' }
    if ($apply.ExitCode -eq 0) { throw 'Updater accepted a running MCP process' }
    if (!(Select-String -LiteralPath "$testRoot/mcp-lock.log" -SimpleMatch 'Close the application before updating')) { throw 'Apply failed for an unexpected reason' }
    if (!(Get-Process -Id $childPid -ErrorAction SilentlyContinue)) { throw 'Updater killed the MCP process' }
    $mcp.StandardInput.WriteLine('close'); $mcp.StandardInput.Flush()
    if (!$mcp.WaitForExit(10000)) { throw 'MCP launcher did not return when the server closed' }
} finally { $mcp.StandardInput.Dispose(); $mcp.Dispose() }
# External file lock (the process image itself is outside the package).
Expand-Archive "$testRoot/v1.zip" "$testRoot/external"
$lockedFile = [IO.File]::Open("$testRoot/external/current/DockPad.UpdateProbe.dll", 'Open', 'Read', 'None')
try {
    $apply = Start-Process -FilePath "$testRoot/external/Update.exe" -ArgumentList '--silent','--log',"`"$testRoot/external-lock.log`"",'apply','--norestart','--package',"`"$feed/DockPad.UpdateProbe-1.0.1-full.nupkg`"" -WindowStyle Hidden -PassThru
    if (!$apply.WaitForExit(45000)) { throw 'External-lock apply timed out' }
    if ($apply.ExitCode -eq 0) { throw 'Updater ignored an exclusive file lock' }
    if (([xml](Get-Content "$testRoot/external/current/sq.version")).package.metadata.version -ne '1.0.0') { throw 'Failed update damaged the old version' }
} finally { $lockedFile.Dispose() }
$apply = Start-Process -FilePath "$testRoot/external/Update.exe" -ArgumentList '--silent','apply','--norestart','--package',"`"$feed/DockPad.UpdateProbe-1.0.1-full.nupkg`"" -WindowStyle Hidden -PassThru
if (!$apply.WaitForExit(45000) -or $apply.ExitCode -ne 0) { throw 'Retry after releasing the file lock failed' }
# WinGet's essential contract: silent install/upgrade and a stable HKCU ARP identity.
$arp = 'HKCU:/Software/Microsoft/Windows/CurrentVersion/Uninstall/DockPad.UpdateProbe'
if (Test-Path $arp) { throw 'A prior probe installation exists; refusing to overwrite it' }
try {
    foreach ($setup in @("$testRoot/v1-Setup.exe", "$feed/DockPad.UpdateProbe-win-Setup.exe")) {
        $install = Start-Process -FilePath $setup -ArgumentList '--silent','--installto',"`"$testRoot/installed`"" -WindowStyle Hidden -PassThru
        if (!$install.WaitForExit(45000) -or $install.ExitCode -ne 0) { throw 'Silent Setup failed' }
        $expected = if ($setup -like '*v1-Setup.exe') { '1.0.0' } else { '1.0.1' }
        $entry = Get-ItemProperty $arp
        if ($entry.DisplayVersion -ne $expected -or $entry.Publisher -ne 'syl-craft' -or $entry.DisplayName -ne 'DockPad.UpdateProbe') { throw 'ARP identity/version mismatch' }
    }
} finally {
    if (Test-Path "$testRoot/installed/Update.exe") {
        $uninstall = Start-Process -FilePath "$testRoot/installed/Update.exe" -ArgumentList '--uninstall','--silent' -WindowStyle Hidden -PassThru
        if (!$uninstall.WaitForExit(45000) -or $uninstall.ExitCode -ne 0) { throw "Probe uninstall failed: $testRoot/installed" }
    }
}
if (Test-Path $arp) { throw 'Probe ARP entry survived uninstall' }
Write-Host "PASS: N to N+1, restart, URL handoff, profile preservation, MCP stdio, consent, external lock/retry, silent Setup upgrade and ARP identity. Artifacts: $testRoot"
