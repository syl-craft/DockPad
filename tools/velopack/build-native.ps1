param(
    [string]$Output = "$PSScriptRoot/../../.tools/velopack-native",
    [string]$Source,
    # Version of the adapted updater, independent of the DockPad application version.
    [ValidatePattern('^\d+\.\d+\.\d+$')][string]$UpdaterVersion = '1.25.0'
)
$ErrorActionPreference = 'Stop'
# Pinned together with the NuGet SDK and vpk. No changes to a global vpk installation.
$commit = '92d6a1c91716729d449034df5c50307dcce39493'
$Output = [IO.Path]::GetFullPath($Output)
$source = if ($Source) { [IO.Path]::GetFullPath($Source) } else { Join-Path $Output 'source' }
New-Item -ItemType Directory -Force $Output | Out-Null
if (!(Test-Path "$source/.git")) {
    git clone --no-checkout https://github.com/velopack/velopack.git $source
    if ($LASTEXITCODE) { throw 'Cannot fetch Velopack source' }
}
git -C $source checkout --detach $commit
if ($LASTEXITCODE) { throw 'Cannot select the pinned Velopack revision' }
git -C $source apply --check "$PSScriptRoot/no-force-stop.patch"
if ($LASTEXITCODE -eq 0) {
    git -C $source apply "$PSScriptRoot/no-force-stop.patch"
} else {
    git -C $source apply --reverse --check "$PSScriptRoot/no-force-stop.patch"
    if ($LASTEXITCODE) { throw 'Unexpected native source changes' }
}
Copy-Item -LiteralPath "$PSScriptRoot/stub.rs" -Destination "$source/src/bins/src/stub.rs"
git -C $source apply --check "$PSScriptRoot/updater-branding.patch"
if ($LASTEXITCODE -eq 0) {
    git -C $source apply "$PSScriptRoot/updater-branding.patch"
    if ($LASTEXITCODE) { throw 'Cannot apply updater branding' }
} else {
    git -C $source apply --reverse --check "$PSScriptRoot/updater-branding.patch"
    if ($LASTEXITCODE) { throw 'Unexpected updater resource changes' }
}
Push-Location $source
$previousFlags = $env:RUSTFLAGS
$previousUpdaterVersion = $env:DOCKPAD_UPDATER_VERSION
try {
    $env:RUSTFLAGS = "$previousFlags -C target-feature=+crt-static".Trim()
    $env:DOCKPAD_UPDATER_VERSION = $null
    cargo build --locked --release -p velopack_bins --features windows --bin update --bin setup --bin stub
    if ($LASTEXITCODE) { throw 'Velopack native build failed (Rust and MSVC C++ tools required)' }
    foreach ($binary in @('update', 'setup', 'stub')) {
        Copy-Item "target/release/$binary.exe" "$Output/${binary}_x64.exe"
    }
    # Build only update with DockPad branding, before packaging/signing. Keep attribution.
    $env:DOCKPAD_UPDATER_VERSION = $UpdaterVersion
    cargo build --locked --release -p velopack_bins --features windows --bin update
    if ($LASTEXITCODE) { throw 'Branded updater build failed' }
    Copy-Item 'target/release/update.exe' "$Output/update_x64.exe"
    $metadata = (Get-Item "$Output/update_x64.exe").VersionInfo
    if ($metadata.ProductName -ne 'DockPad-Updater' -or $metadata.ProductVersion -ne $UpdaterVersion -or $metadata.FileVersion -ne $UpdaterVersion -or $metadata.FileDescription -ne "DockPad-Updater $UpdaterVersion") {
        throw 'Updater branding verification failed'
    }
    Copy-Item LICENSE "$Output/Velopack.LICENSE.txt"
    Set-Content "$Output/source-revision.txt" $commit
} finally {
    $env:RUSTFLAGS = $previousFlags
    $env:DOCKPAD_UPDATER_VERSION = $previousUpdaterVersion
    Pop-Location
}
