param([string]$TestPath = (Join-Path (Split-Path $PSScriptRoot -Parent) 'artifacts/CodexMeter-framework-x64/CodexMeter.Tests.exe'))
$ErrorActionPreference = 'Stop'
$fixtureRoot = [IO.Path]::GetFullPath((Join-Path (Split-Path $PSScriptRoot -Parent) 'artifacts/test-data'))
[IO.Directory]::CreateDirectory($fixtureRoot) | Out-Null
$fixtureDirectory = [IO.Path]::GetFullPath((Join-Path $fixtureRoot ('release-script-' + [Guid]::NewGuid().ToString('N'))))
if ([IO.Path]::GetDirectoryName($fixtureDirectory) -ne $fixtureRoot) { throw 'Unsafe fixture directory.' }
try {
    & $TestPath --release-script-fixture $fixtureDirectory
    if ($LASTEXITCODE -ne 0) { throw 'Could not create an isolated test-only key and package.' }
    Add-Type -AssemblyName System.IO.Compression
    Add-Type -AssemblyName System.IO.Compression.FileSystem
    $singleArchive = Join-Path $fixtureDirectory 'single.zip'
    Copy-Item -LiteralPath (Join-Path $fixtureDirectory 'fixture.zip') -Destination $singleArchive
    $single = [IO.Compression.ZipFile]::Open($singleArchive, [IO.Compression.ZipArchiveMode]::Update)
    try { $single.GetEntry('CodexMeter.Core.dll').Delete() } finally { $single.Dispose() }
    foreach ($channel in @('stable','beta')) {
        $version = if ($channel -eq 'stable') { '9.9.9' } else { '9.9.9-beta.1' }
        foreach ($layout in @('framework','dotnet-legacy','dotnet-single')) {
            $runtime = if ($layout -eq 'framework') { 'framework-x64' } else { 'dotnet10-x64' }
            $archive = if ($layout -eq 'dotnet-single') { $singleArchive } else { Join-Path $fixtureDirectory 'fixture.zip' }
            & (Join-Path $PSScriptRoot 'sign-release.ps1') -Archive $archive -Version $version -Channel $channel -Runtime $runtime -PackageUrl 'https://example.test/fixture.zip' -PrivateKeyFile (Join-Path $fixtureDirectory 'TEST-ONLY-private.xml') -OutputDirectory $fixtureDirectory
            & $TestPath --verify-release (Join-Path $fixtureDirectory "$channel-$runtime.json") (Join-Path $fixtureDirectory 'public-key.json') $archive $channel $runtime
            if ($LASTEXITCODE -ne 0) { throw 'Publisher output did not verify in the application core.' }
            Write-Output ('PASS publisher to application verification: ' + $channel + '/' + $layout)
        }
    }
    Write-Output 'PASS publisher script stable/beta manifests verify with the application; isolated test key only'
} finally {
    # Check the resolved full target before recursive fixture cleanup.
    $resolvedFixture = [IO.Path]::GetFullPath($fixtureDirectory)
    if ([IO.Path]::GetDirectoryName($resolvedFixture) -eq $fixtureRoot -and (Test-Path -LiteralPath $resolvedFixture)) {
        Remove-Item -LiteralPath $resolvedFixture -Recurse -Force
    }
}
