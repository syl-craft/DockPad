# Apps sélectionnées depuis le Start Menu (Win32 uniquement, icônes extraites automatiquement)
$apps = @(
    @{ Name = "7-Zip";                   Exe = "C:\Program Files\7-Zip\7zFM.exe" },
    @{ Name = "Arduino IDE";             Exe = "C:\Users\Sylvain\AppData\Local\Programs\Arduino IDE\Arduino IDE.exe" },
    @{ Name = "Autodesk Fusion";         Exe = "C:\Users\Sylvain\AppData\Local\Autodesk\webdeploy\production\6a0c9611291d45bb9226980209917c3d\FusionLauncher.exe" },
    @{ Name = "Azure Cosmos DB";         Exe = "C:\Program Files\Azure Cosmos DB Emulator\Microsoft.Azure.Cosmos.Emulator.exe" },
    @{ Name = "Azure Storage Explorer";  Exe = "C:\Program Files\Microsoft Azure Storage Explorer\StorageExplorer.exe" },
    @{ Name = "Bambu Studio";            Exe = "C:\Program Files\Bambu Studio\bambu-studio.exe" },
    @{ Name = "Bitwarden";               Exe = "C:\Users\Sylvain\AppData\Local\Programs\Bitwarden\Bitwarden.exe" },
    @{ Name = "DBeaver";                 Exe = "C:\Users\Sylvain\AppData\Local\DBeaver\dbeaver.exe" },
    @{ Name = "Everything";              Exe = "C:\Program Files\Everything\Everything.exe" },
    @{ Name = "Excel";                   Exe = "C:\Program Files\Microsoft Office\root\Office16\EXCEL.EXE" },
    @{ Name = "Git Bash";                Exe = "C:\Program Files\Git\git-bash.exe" },
    @{ Name = "GitHub Desktop";          Exe = "C:\Users\Sylvain\AppData\Local\GitHubDesktop\GitHubDesktop.exe" },
    @{ Name = "Google Chrome";           Exe = "C:\Program Files\Google\Chrome\Application\chrome.exe" },
    @{ Name = "Inkscape";                Exe = "C:\Program Files\Inkscape\bin\inkscape.exe" },
    @{ Name = "MobaXterm";               Exe = "C:\Program Files (x86)\Mobatek\MobaXterm\MobaXterm.exe" },
    @{ Name = "Notepad++";               Exe = "C:\Program Files\Notepad++\notepad++.exe" },
    @{ Name = "OrcaSlicer";              Exe = "C:\Program Files\OrcaSlicer\orca-slicer.exe" },
    @{ Name = "Outlook";                 Exe = "C:\Program Files\Microsoft Office\root\Office16\OUTLOOK.EXE" },
    @{ Name = "PicPick";                 Exe = "C:\Program Files (x86)\PicPick\picpick.exe" },
    @{ Name = "Postman";                 Exe = "C:\Users\Sylvain\AppData\Local\Postman\Postman.exe" },
    @{ Name = "PowerPoint";              Exe = "C:\Program Files\Microsoft Office\root\Office16\POWERPNT.EXE" },
    @{ Name = "PrusaSlicer";             Exe = "C:\Program Files\Prusa3D\PrusaSlicer\prusa-slicer.exe" },
    @{ Name = "Redis Desktop Manager";   Exe = "C:\Program Files (x86)\RedisDesktopManager\rdm.exe" },
    @{ Name = "Slack";                   Exe = "C:\Users\Sylvain\AppData\Local\slack\slack.exe" },
    @{ Name = "SSMS 20";                 Exe = "C:\Program Files (x86)\Microsoft SQL Server Management Studio 20\Common7\IDE\Ssms.exe" },
    @{ Name = "Stream Dock";             Exe = "C:\Program Files (x86)\StreamDock\StreamDock.exe" },
    @{ Name = "Visual Studio 2022";      Exe = "C:\Program Files\Microsoft Visual Studio\2022\Professional\Common7\IDE\devenv.exe" },
    @{ Name = "VS Code";                 Exe = "C:\Users\Sylvain\AppData\Local\Programs\Microsoft VS Code\Code.exe" },
    @{ Name = "VLC";                     Exe = "C:\Program Files\VideoLAN\VLC\vlc.exe" },
    @{ Name = "WinDirStat";              Exe = "C:\Program Files (x86)\WinDirStat\windirstat.exe" },
    @{ Name = "WinMerge";                Exe = "C:\Program Files\WinMerge\WinMergeU.exe" },
    @{ Name = "Word";                    Exe = "C:\Program Files\Microsoft Office\root\Office16\WINWORD.EXE" }
)

# Filtrer les exes qui n'existent pas
$apps = $apps | Where-Object { Test-Path $_.Exe }
Write-Host "$($apps.Count) apps trouvées"

# Chemins DockPad
$profileRoot = [IO.Path]::Combine($env:APPDATA, 'DockPad')
$iconsDir    = [IO.Path]::Combine($profileRoot, 'icons')
$jsonPath    = [IO.Path]::Combine($profileRoot, 'shortcuts.json')
[IO.Directory]::CreateDirectory($iconsDir) | Out-Null

Add-Type -AssemblyName System.Drawing

function Cache-Icon($exePath) {
    try {
        $icon = [System.Drawing.Icon]::ExtractAssociatedIcon($exePath)
        $bmp  = $icon.ToBitmap()
        $ms   = [IO.MemoryStream]::new()
        $bmp.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png)
        $bytes = $ms.ToArray()
        $ms.Dispose(); $bmp.Dispose(); $icon.Dispose()

        $sha1    = [Security.Cryptography.SHA1]::Create()
        $hash    = $sha1.ComputeHash($bytes)
        $hashStr = [BitConverter]::ToString($hash).Replace('-','').ToLower()
        $sha1.Dispose()

        $rel = "icons\$hashStr.png"
        $abs = [IO.Path]::Combine($profileRoot, $rel)
        if (-not [IO.File]::Exists($abs)) { [IO.File]::WriteAllBytes($abs, $bytes) }
        return $rel
    } catch { return $null }
}

# Lire shortcuts.json existant
$existing = @()
if (Test-Path $jsonPath) {
    $existing = Get-Content $jsonPath -Raw | ConvertFrom-Json
}

# Trouver la première page libre
$maxPage = -1
if ($existing.Count -gt 0) {
    $maxPage = ($existing | Measure-Object -Property page -Maximum).Maximum
}
$startPage = $maxPage + 1

# Construire les nouvelles entrées (grille 4×6)
$COLS = 6
$newEntries = @()
$i = 0
foreach ($app in $apps) {
    $row = [Math]::Floor($i / $COLS)
    $col = $i % $COLS
    $page = $startPage + [Math]::Floor($row / 4)
    $row  = $row % 4

    Write-Host "  [$page,$row,$col] $($app.Name)"
    $iconRel = Cache-Icon $app.Exe

    $entry = [PSCustomObject]@{
        page          = $page
        row           = $row
        col           = $col
        name          = $app.Name
        type          = "SwitchToProcess"
        command       = [IO.Path]::GetFileName($app.Exe)
        iconPath      = $app.Exe
        iconProfilePath = $iconRel
        processSwitch = [PSCustomObject]@{
            processName = [IO.Path]::GetFileName($app.Exe)
            executable  = $app.Exe
            parameters  = ""
        }
    }
    $newEntries += $entry
    $i++
}

# Fusionner et sauvegarder
$all = @($existing) + $newEntries
$all | ConvertTo-Json -Depth 5 | Set-Content $jsonPath -Encoding UTF8
$lastPage = $startPage + [Math]::Floor(($apps.Count - 1) / 24)
Write-Host "OK : $($newEntries.Count) raccourcis ajoutes sur les pages $startPage a $lastPage"
