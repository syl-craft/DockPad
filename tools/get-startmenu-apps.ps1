$guids = @{
    '{6D809377-6AF0-444B-8957-A3773F02200E}' = $env:ProgramW6432
    '{7C5A40EF-A0FB-4BFC-874A-C0F2E0B9FA8E}' = ${env:ProgramFiles(x86)}
    '{1AC14E77-02E7-4E5D-B744-2EB1AE5198B7}' = [IO.Path]::Combine($env:SystemRoot, 'System32')
    '{F38BF404-1D43-42F2-9305-67DE0B28FC23}' = $env:SystemRoot
}

function Resolve-AppId($appId) {
    if ([IO.File]::Exists($appId)) { return $appId }
    foreach ($guid in $guids.Keys) {
        if ($appId.StartsWith($guid, [StringComparison]::OrdinalIgnoreCase)) {
            $rel  = $appId.Substring($guid.Length).TrimStart('\')
            $full = [IO.Path]::Combine($guids[$guid], $rel)
            if ([IO.File]::Exists($full)) { return $full }
        }
    }
    return $null
}

$shell   = New-Object -ComObject WScript.Shell
$apps    = Get-StartApps | Where-Object { $_.AppID -notmatch '!' }
$results = @()

foreach ($app in $apps) {
    $path = Resolve-AppId $app.AppID
    if (-not $path) {
        foreach ($base in @(
            [IO.Path]::Combine($env:APPDATA, 'Microsoft\Windows\Start Menu\Programs'),
            'C:\ProgramData\Microsoft\Windows\Start Menu\Programs'
        )) {
            $lnk = Get-ChildItem $base -Recurse -Filter '*.lnk' -ErrorAction SilentlyContinue |
                   Where-Object { $_.BaseName -like "*$($app.Name)*" } |
                   Select-Object -First 1
            if ($lnk) {
                $sc = $shell.CreateShortcut($lnk.FullName)
                if ([IO.File]::Exists($sc.TargetPath)) { $path = $sc.TargetPath; break }
            }
        }
    }
    if ($path -and $path -match '\.exe$') {
        $results += [PSCustomObject]@{ Name = $app.Name; Exe = $path }
    }
}

$results | Sort-Object Name | ConvertTo-Json
