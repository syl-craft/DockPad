param([string]$ApplicationDirectory = "$PSScriptRoot/../../.tools/signing-validation")

BeforeAll {
    $script:application = [IO.Path]::GetFullPath($ApplicationDirectory)
    $script:verifyApplication = "$PSScriptRoot/verify-application.ps1"
    $script:verifyRelease = "$PSScriptRoot/verify-release.ps1"
    $script:pack = "$PSScriptRoot/../velopack/pack.ps1"
    $script:thumbprint = '1234567890123456789012345678901234567890'
    $script:files = @('DockPad.exe', 'DockPad.dll', 'DockPad.Core.dll', 'fr/DockPad.Core.resources.dll', 'qps-Ploc/DockPad.Core.resources.dll')
    foreach ($file in $files) {
        if (!(Test-Path "$application/$file")) { throw 'Publish the application before running signing tests (see docs/code-signing.md).' }
    }
    function New-Fixture {
        $folder = Join-Path $TestDrive ([Guid]::NewGuid().ToString('N'))
        foreach ($file in $files) {
            $target = Join-Path $folder $file
            New-Item -ItemType Directory -Force ([IO.Path]::GetDirectoryName($target)) | Out-Null
            Copy-Item "$application/$file" $target
        }
        return $folder
    }
    function Trusted-Signature {
        [pscustomobject]@{ Status = 'Valid'; SignerCertificate = [pscustomobject]@{ Thumbprint = $thumbprint }; TimeStamperCertificate = [pscustomobject]@{ Subject = 'test timestamp' } }
    }
}

Describe 'Application signature gates (certificate responses mocked; real published PE files)' {
    BeforeEach { $script:fixture = New-Fixture }
    It 'rejects actual unsigned binaries' {
        { & $verifyApplication -Directory $fixture -Version 1.24.0 -CertificateThumbprint $thumbprint } | Should -Throw '*signature missing*'
    }
    It 'accepts all expected binaries only with a trusted timestamped expected certificate' {
        Mock Get-AuthenticodeSignature { Trusted-Signature }
        { & $verifyApplication -Directory $fixture -Version 1.24.0 -CertificateThumbprint $thumbprint } | Should -Not -Throw
        Should -Invoke Get-AuthenticodeSignature -Times 5 -Exactly
    }
    It 'rejects a different trusted publisher' {
        Mock Get-AuthenticodeSignature { $s = Trusted-Signature; $s.SignerCertificate.Thumbprint = '0000000000000000000000000000000000000000'; $s }
        { & $verifyApplication -Directory $fixture -Version 1.24.0 -CertificateThumbprint $thumbprint } | Should -Throw '*signature missing*'
    }
    It 'rejects an absent timestamp' {
        Mock Get-AuthenticodeSignature { $s = Trusted-Signature; $s.TimeStamperCertificate = $null; $s }
        { & $verifyApplication -Directory $fixture -Version 1.24.0 -CertificateThumbprint $thumbprint } | Should -Throw '*signature missing*'
    }
    It 'rejects invalid signatures' {
        Mock Get-AuthenticodeSignature { $s = Trusted-Signature; $s.Status = 'HashMismatch'; $s }
        { & $verifyApplication -Directory $fixture -Version 1.24.0 -CertificateThumbprint $thumbprint } | Should -Throw '*signature missing*'
    }
    It 'rejects a missing satellite assembly' {
        Mock Get-AuthenticodeSignature { Trusted-Signature }
        Remove-Item -LiteralPath "$fixture/fr/DockPad.Core.resources.dll"
        { & $verifyApplication -Directory $fixture -Version 1.24.0 -CertificateThumbprint $thumbprint } | Should -Throw '*file missing*'
    }
    It 'rejects metadata from another version' {
        Mock Get-AuthenticodeSignature { Trusted-Signature }
        { & $verifyApplication -Directory $fixture -Version 9.9.9 -CertificateThumbprint $thumbprint } | Should -Throw '*metadata*'
    }
    It 'rejects a release request before building when the signer is not configured' {
        { & $pack -RequireSigned -CertificateThumbprint $thumbprint } | Should -Throw '*requires a signing command*'
    }
}

Describe 'Release consistency gates (certificate responses mocked)' {
    BeforeEach {
        Mock Get-AuthenticodeSignature { Trusted-Signature }
        $script:release = Join-Path $TestDrive ([Guid]::NewGuid().ToString('N'))
        New-Item -ItemType Directory $release | Out-Null
        $payload = New-Fixture
        $portable = "$release/portable"
        New-Item -ItemType Directory $portable | Out-Null
        Copy-Item -LiteralPath $payload -Destination "$portable/current" -Recurse
        Copy-Item "$application/DockPad.exe" "$portable/DockPad.exe"
        Copy-Item "$application/DockPad.exe" "$portable/Update.exe"
        [IO.Compression.ZipFile]::CreateFromDirectory($portable, "$release/DockPad-win-Portable.zip")
        $package = "$release/package"
        New-Item -ItemType Directory "$package/lib" -Force | Out-Null
        Copy-Item -LiteralPath $payload -Destination "$package/lib/app" -Recurse
        Copy-Item "$application/DockPad.exe" "$package/lib/app/Squirrel.exe"
        Copy-Item "$application/DockPad.exe" "$package/lib/app/DockPad_ExecutionStub.exe"
        [IO.Compression.ZipFile]::CreateFromDirectory($package, "$release/DockPad-1.24.0-full.nupkg")
        Copy-Item "$application/DockPad.exe" "$release/DockPad-win-Setup.exe"
        Copy-Item "$application/DockPad.exe" "$release/DockPad-1.24.0-win-x64-Setup.exe"
        $full = "$release/DockPad-1.24.0-full.nupkg"
        $script:feed = @{ Assets = @(@{ Version = '1.24.0'; Type = 'Full'; FileName = 'DockPad-1.24.0-full.nupkg'; Size = (Get-Item $full).Length; SHA1 = (Get-FileHash $full -Algorithm SHA1).Hash; SHA256 = (Get-FileHash $full).Hash }) }
        $feed | ConvertTo-Json -Depth 4 | Set-Content "$release/releases.win.json"
    }
    It 'verifies both payloads, the native entry points and feed' {
        { & $verifyRelease -Directory $release -Version 1.24.0 -CertificateThumbprint $thumbprint } | Should -Not -Throw
        Should -Invoke Get-AuthenticodeSignature -Times 16 -Exactly
    }
    It 'rejects a stale feed after signing' {
        $feed.Assets[0].SHA256 = '0' * 64
        $feed | ConvertTo-Json -Depth 4 | Set-Content "$release/releases.win.json"
        { & $verifyRelease -Directory $release -Version 1.24.0 -CertificateThumbprint $thumbprint } | Should -Throw '*Feed mismatch*'
    }
    It 'rejects differing Setup aliases' {
        Add-Content "$release/DockPad-win-Setup.exe" 'changed'
        { & $verifyRelease -Directory $release -Version 1.24.0 -CertificateThumbprint $thumbprint } | Should -Throw '*aliases differ*'
    }
    It 'rejects an unsigned updater even when the application is signed' {
        Mock Get-AuthenticodeSignature { $s = Trusted-Signature; if ($LiteralPath -like '*Update.exe') { $s.Status = 'NotSigned' }; $s }
        { & $verifyRelease -Directory $release -Version 1.24.0 -CertificateThumbprint $thumbprint } | Should -Throw '*publisher signature*'
    }
}
