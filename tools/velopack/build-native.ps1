param([string]$Output = "$PSScriptRoot/../../.tools/velopack-native")
$ErrorActionPreference = 'Stop'
# Pinned together with the NuGet SDK and vpk. No changes to a global vpk installation.
$commit = '92d6a1c91716729d449034df5c50307dcce39493'
$Output = [IO.Path]::GetFullPath($Output)
$source = Join-Path $Output 'source'
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
Push-Location $source
$previousFlags = $env:RUSTFLAGS
try {
    $env:RUSTFLAGS = "$previousFlags -C target-feature=+crt-static".Trim()
    cargo build --locked --release -p velopack_bins --features windows --bin update --bin setup --bin stub
    if ($LASTEXITCODE) { throw 'Velopack native build failed (Rust and MSVC C++ tools required)' }
    foreach ($binary in @('update', 'setup', 'stub')) {
        Copy-Item "target/release/$binary.exe" "$Output/${binary}_x64.exe"
    }
    Copy-Item LICENSE "$Output/Velopack.LICENSE.txt"
    Set-Content "$Output/source-revision.txt" $commit
} finally { $env:RUSTFLAGS = $previousFlags; Pop-Location }
