#Requires -Version 7
# Publie la version de DockPad.csproj depuis le PC : fabrication à partir du commit, release GitHub en brouillon,
# vérification de la liste exacte des fichiers, puis passage en latest. Voir docs/updates.md, « Publier une version ».
param(
    [switch]$DryRun,
    [switch]$Draft,
    [switch]$SkipTests,
    [string]$Title,
    # Seulement avec -DryRun : essayer la chaîne sur une version qui n'a ni tag ni section de CHANGELOG.
    [string]$Version,
    [string]$Native = "$PSScriptRoot/../../.tools/velopack-native"
)
$ErrorActionPreference = 'Stop'
$PSNativeCommandUseErrorActionPreference = $false
$repo = [IO.Path]::GetFullPath("$PSScriptRoot/../..")
$Native = [IO.Path]::GetFullPath($Native)
$gitHub = 'syl-craft/DockPad'
Set-Location $repo

function Invoke-Checked([string]$what, [scriptblock]$command) {
    $output = & $command
    if ($LASTEXITCODE) { throw "$what : code $LASTEXITCODE" }
    $output
}

# 1. Préconditions, sans rien écrire.
if ($Version -and !$DryRun) { throw '-Version est réservé à -DryRun : une publication lit la version de DockPad.csproj.' }
$commit = Invoke-Checked 'git rev-parse' { git rev-parse HEAD }
$project = [xml](Invoke-Checked 'git show' { git show "${commit}:DockPad.csproj" })
if (!$Version) { $Version = $project.Project.PropertyGroup.Version | Where-Object { $_ } | Select-Object -First 1 }
if ($Version -notmatch '^\d+\.\d+\.\d+$') { throw "Version stable x.y.z attendue : '$Version'" }
$tag = "v$Version"

$changelog = (Invoke-Checked 'git show' { git show "${commit}:CHANGELOG.md" }) -join "`n"
$section = [regex]::Match($changelog, '(?ms)^## \[' + [regex]::Escape($Version) + '\][^\n]*\n(.*?)(?=^## \[|\z)')
if (!$section.Success -and !$DryRun) { throw "CHANGELOG.md n'a pas de section [$Version]" }
$notes = if ($section.Success) { "## DockPad $Version`n`n" + $section.Groups[1].Value.Trim() + "`n" }
         else { Write-Warning "Pas de section [$Version] dans CHANGELOG.md : notes factices (DryRun)."; "## DockPad $Version`n`n(essai)`n" }

$props = [xml](Invoke-Checked 'git show' { git show "${commit}:Directory.Build.props" })
$mueVersion = $props.Project.PropertyGroup.MueVersion | ForEach-Object { if ($_ -is [string]) { $_ } else { $_.'#text' } } |
    Where-Object { $_ } | Select-Object -First 1
$mueFeed = 'C:\Users\Sylvain\nuget-local'
foreach ($package in 'Mue', 'Mue.Velopack', 'Mue.Wpf') {
    if (!(Test-Path -LiteralPath "$mueFeed/$package.$mueVersion.nupkg")) { throw "Paquet absent de $mueFeed : $package $mueVersion" }
}
if ($mueVersion -notmatch '^\d+\.\d+\.\d+$') { throw "Mue doit être une version stable : $mueVersion" }
if ((Get-Content -LiteralPath "$Native/source-revision.txt" -Raw).Trim() -ne '92d6a1c91716729d449034df5c50307dcce39493') {
    throw "Binaires natifs adaptés absents ou d'une autre révision : $Native (tools/velopack/build-native.ps1)"
}

Invoke-Checked 'gh auth status' { gh auth status 2>&1 } | Out-Null
Invoke-Checked 'git fetch' { git fetch --quiet origin } | Out-Null
$pushed = (Invoke-Checked 'git rev-parse' { git rev-parse origin/master }) -eq $commit
if (!$pushed) {
    if (!$DryRun) { throw "HEAD ($commit) n'est pas origin/master : pousser master avant de publier." }
    Write-Warning 'HEAD ne correspond pas à origin/master : accepté pour un DryRun seulement.'
}
if (git tag --list $tag) { throw "Le tag $tag existe déjà en local." }
if (Invoke-Checked 'git ls-remote' { git ls-remote --tags origin "refs/tags/$tag" }) { throw "Le tag $tag existe déjà sur origin." }
$tracked = Invoke-Checked 'git status' { git status --porcelain --untracked-files=no }
if ($tracked) { Write-Warning "Modifications locales ignorées : la fabrication part du commit $($commit.Substring(0, 7)), pas du dossier." }

# 2. Fabrication depuis le commit seul, dans un dossier neuf.
$work = Join-Path $repo "release/publication/$Version"
if (Test-Path -LiteralPath $work) { throw "Dossier d'une tentative précédente : $work. L'examiner puis le supprimer avant de relancer." }
$source = "$work/source"
$out = "$work/out"
New-Item -ItemType Directory -Force $source, $out | Out-Null
$state = [ordered]@{ version = $Version; commit = $commit; tag = $tag; dryRun = [bool]$DryRun; steps = @() }
function Save-State([string]$step) {
    $state.steps += "$([DateTime]::UtcNow.ToString('o')) $step"
    $state | ConvertTo-Json | Set-Content -LiteralPath "$work/publication-state.json" -Encoding utf8
}
Save-State 'préconditions vérifiées'

Invoke-Checked 'git archive' { git archive --format=tar -o "$work/source.tar" $commit } | Out-Null
Invoke-Checked 'tar' { tar -xf "$work/source.tar" -C $source } | Out-Null
Remove-Item -LiteralPath "$work/source.tar"

if (!$SkipTests) {
    foreach ($tests in 'DockPad.Tests/DockPad.Tests.csproj', 'DockPad.Tests.Wpf/DockPad.Tests.Wpf.csproj') {
        Invoke-Checked "dotnet test $tests" { dotnet test "$source/$tests" -c Release -m:4 "-p:MueFeed=$mueFeed" --filter 'Category!=RealMachine' } |
            Select-Object -Last 2 | Write-Host
    }
}

# pack.ps1 installe vpk dans le dossier extrait ; l'installer d'abord permet de télécharger le précédent.
$vpkRoot = "$source/.tools/vpk"
Invoke-Checked 'dotnet tool install vpk' { dotnet tool install vpk --version 1.2.161 --tool-path $vpkRoot } | Out-Null
# Une erreur de lecture de GitHub arrête tout : elle ne vaut jamais « première release, sans delta ».
Invoke-Checked 'vpk download' { & "$vpkRoot/vpk.exe" download github --repoUrl "https://github.com/$gitHub" --channel win --outputDir $out } |
    Write-Host
$previous = Get-ChildItem -LiteralPath $out -Filter 'DockPad-*-full.nupkg' | Select-Object -ExpandProperty Name
if (!$previous) { throw "Aucun paquet précédent téléchargé dans $out" }

# Sans section de CHANGELOG (DryRun sur une version d'essai), pack.ps1 reçoit les notes factices.
$packNotes = if ($section.Success) { @{} } else { [IO.File]::WriteAllText("$work/pack-notes.md", $notes); @{ ReleaseNotes = "$work/pack-notes.md" } }
& "$source/tools/velopack/pack.ps1" -Version $Version -Output $out -Native $Native @packNotes
Save-State 'paquets fabriqués'

# 3. Liste exacte des fichiers publiés : ceux de cette version, plus le flux et les installateurs à nom fixe.
$names = @('releases.win.json', 'assets.win.json', 'RELEASES', 'DockPad-win-Setup.exe', 'DockPad-win-Portable.zip',
    "DockPad-$Version-full.nupkg", "DockPad-$Version-delta.nupkg", "DockPad-$Version-win-x64-Setup.exe")
foreach ($name in $names) { if (!(Test-Path -LiteralPath "$out/$name")) { throw "Fichier attendu absent : $name" } }
$feed = Get-Content -LiteralPath "$out/releases.win.json" -Raw | ConvertFrom-Json
$current = @($feed.Assets | Where-Object { $_.Version -eq $Version })
if ($current.Count -ne 2 -or ($current.FileName | Where-Object { $_ -notin $names })) {
    throw "releases.win.json ne référence pas exactement le full et le delta de $Version"
}
foreach ($asset in $current) {
    $file = Get-Item -LiteralPath "$out/$($asset.FileName)"
    if ($file.Length -ne $asset.Size) { throw "Taille différente du flux : $($asset.FileName)" }
    if ((Get-FileHash -LiteralPath $file -Algorithm SHA1).Hash -ne $asset.SHA1) { throw "SHA1 différent du flux : $($asset.FileName)" }
}
$publish = "$work/publish"
New-Item -ItemType Directory $publish | Out-Null
foreach ($name in $names) { Copy-Item -LiteralPath "$out/$name" -Destination $publish }
# Même format que les releases précédentes : empreinte, espace, astérisque, nom ; fin de ligne LF.
$sums = Get-ChildItem -LiteralPath $publish | Sort-Object Name |
    ForEach-Object { "$((Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash.ToLowerInvariant()) *$($_.Name)" }
[IO.File]::WriteAllText("$publish/SHA256SUMS.txt", ($sums -join "`n") + "`n")
$notesFile = "$work/notes.md"
[IO.File]::WriteAllText($notesFile, $notes)
$releaseTitle = if ($Title) { "DockPad $Version — $Title" } else { "DockPad $Version" }
$files = Get-ChildItem -LiteralPath $publish | Sort-Object Name

Write-Host "`nDockPad $Version, commit $($commit.Substring(0, 7)), Mue $mueVersion, précédent : $($previous -join ', ')"
Write-Host "Titre : $releaseTitle"
$files | ForEach-Object { Write-Host ("  {0,-40} {1,12:N0}" -f $_.Name, $_.Length) }
Save-State 'liste vérifiée'
if ($DryRun) {
    Write-Host "`nDryRun : rien n'a été envoyé. Fichiers dans $publish"
    return
}

# 4. Publication, après confirmation.
if ((Read-Host "Taper $Version pour publier sur $gitHub") -ne $Version) { throw 'Publication annulée : rien n''a été envoyé.' }
Invoke-Checked 'git tag' { git tag -a $tag -m "DockPad $Version" $commit } | Out-Null
$tagObject = Invoke-Checked 'git rev-parse' { git rev-parse "refs/tags/$tag" }
try {
    Save-State "push du tag $tagObject demandé"
    Invoke-Checked 'git push' { git push origin "${tagObject}:refs/tags/$tag" } | Out-Null
} catch {
    Write-Warning 'Push du tag en échec : état distant à vérifier (git ls-remote --tags origin). Tag local conservé.'
    throw
}
$remote = Invoke-Checked 'git ls-remote' { git ls-remote --tags origin "refs/tags/$tag" "refs/tags/$tag^{}" }
if (!($remote -match "^$tagObject\s") -or !($remote -match "^$commit\s")) { throw "Tag distant inattendu : $remote" }
Save-State 'tag distant vérifié'

Save-State 'création du brouillon demandée'
Invoke-Checked 'gh release create' {
    gh release create $tag --repo $gitHub --draft --verify-tag --title $releaseTitle --notes-file $notesFile $files.FullName
} | Out-Null
$release = Invoke-Checked 'gh release view' { gh release view $tag --repo $gitHub --json isDraft,assets } | ConvertFrom-Json
$uploaded = $release.assets | ForEach-Object { "$($_.name)|$($_.size)" } | Sort-Object
$expected = $files | ForEach-Object { "$($_.Name)|$($_.Length)" } | Sort-Object
if (!$release.isDraft -or (Compare-Object $uploaded $expected)) { throw "Brouillon incomplet ou inattendu : rien n'est rendu public. Voir $tag sur GitHub." }
Save-State 'brouillon vérifié'
if ($Draft) {
    Write-Host "Brouillon $tag vérifié, non publié."
    return
}

Save-State 'passage en latest demandé'
Invoke-Checked 'gh release edit' { gh release edit $tag --repo $gitHub --draft=false --latest } | Out-Null
Save-State 'release publique'
$latest = Invoke-Checked 'gh api' { gh api "repos/$gitHub/releases/latest" --jq .tag_name }
if ($latest -ne $tag) { throw "Release publiée, mais la latest observée est $latest" }
Save-State 'latest vérifiée'
Write-Host "DockPad $Version publié : https://github.com/$gitHub/releases/tag/$tag"
