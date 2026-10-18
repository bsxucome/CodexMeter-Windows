param(
    [string]$AppPath = (Join-Path (Split-Path $PSScriptRoot -Parent) 'artifacts/CodexMeter-framework-x64/CodexMeter.exe'),
    [string]$TestPath = (Join-Path (Split-Path $PSScriptRoot -Parent) 'artifacts/CodexMeter-framework-x64/CodexMeter.Tests.exe')
)
$ErrorActionPreference = 'Stop'
$fixtureRoot = [IO.Path]::GetFullPath((Join-Path (Split-Path $PSScriptRoot -Parent) 'artifacts/test-data'))
[IO.Directory]::CreateDirectory($fixtureRoot) | Out-Null
foreach ($scenario in @('success','startup-failure','tampered')) {
    $directory = [IO.Path]::GetFullPath((Join-Path $fixtureRoot ('update-process-' + [Guid]::NewGuid().ToString('N'))))
    if ([IO.Path]::GetDirectoryName($directory) -ne $fixtureRoot) { throw 'Unsafe fixture directory.' }
    [IO.Directory]::CreateDirectory($directory) | Out-Null
    $encoding = New-Object Text.UTF8Encoding($false)
    [IO.File]::WriteAllText((Join-Path $directory '.fixture'), 'CodexMeter.UpdateFixture.v1', $encoding)
    [IO.File]::WriteAllText((Join-Path $directory 'startup-mode'), $(if ($scenario -eq 'startup-failure') { 'fail' } else { 'success' }), $encoding)
    $oldDirectory = Join-Path $directory 'old'
    [IO.Directory]::CreateDirectory($oldDirectory) | Out-Null
    $appDirectory = Split-Path ([IO.Path]::GetFullPath($AppPath)) -Parent
    foreach ($name in @('CodexMeter.exe','CodexMeter.Core.dll','CodexMeter.exe.config')) { Copy-Item -LiteralPath (Join-Path $appDirectory $name) -Destination $oldDirectory }
    $old = Join-Path $oldDirectory 'CodexMeter.exe'
    $oldHash = (Get-FileHash -LiteralPath $old -Algorithm SHA256).Hash
    try {
        & $TestPath --release-script-fixture $directory
        if ($LASTEXITCODE -ne 0) { throw 'Fixture key generation failed.' }
        $stage = [Guid]::NewGuid().ToString('N')
        $staging = Join-Path $directory "updates/staging/$stage"
        [IO.Directory]::CreateDirectory($staging) | Out-Null
        $archive = Join-Path $staging 'package.zip'
        Compress-Archive -LiteralPath @((Join-Path $oldDirectory 'CodexMeter.exe'),(Join-Path $oldDirectory 'CodexMeter.Core.dll'),(Join-Path $oldDirectory 'CodexMeter.exe.config')) -DestinationPath $archive
        & (Join-Path $PSScriptRoot 'sign-release.ps1') -Archive $archive -Version 9.9.9 -Channel stable -Runtime framework-x64 -PackageUrl 'https://example.test/fixture.zip' -PrivateKeyFile (Join-Path $directory 'TEST-ONLY-private.xml') -OutputDirectory $directory
        Copy-Item -LiteralPath (Join-Path $directory 'stable-framework-x64.json') -Destination (Join-Path $staging 'manifest.json')
        $settings = @{ updatePublicKey=[IO.File]::ReadAllText((Join-Path $directory 'public-key.json')); updateChannel='stable'; updateFeed='https://example.test/fixture.json' } | ConvertTo-Json -Compress
        [IO.File]::WriteAllText((Join-Path $directory 'settings.json'), $settings, $encoding)
        if ($scenario -eq 'tampered') {
            $stream = [IO.File]::Open($archive, [IO.FileMode]::Append)
            try { $stream.WriteByte(1) } finally { $stream.Dispose() }
        }
        $arguments = '--install-fixture "' + $directory + '" ' + $stage
        $parent = Start-Process -FilePath $old -ArgumentList $arguments -WindowStyle Hidden -PassThru
        if (!$parent.WaitForExit(75000)) { $parent.Kill(); throw 'Fixture parent timed out.' }
        if ($scenario -eq 'tampered') {
            if ($parent.ExitCode -eq 0) { throw 'Tampered archive reached a successful handoff.' }
            if (@(Get-ChildItem -LiteralPath $directory -Directory -Filter 'CodexMeter-Windows-*').Count) { throw 'Tampered archive created an installation.' }
            Write-Output 'PASS real update helper rejects tampering before parent handoff'
        } else {
            if ($parent.ExitCode -ne 0) { throw 'Fixture helper preparation failed.' }
            $resultPath = Join-Path $directory 'updates/last-result.json'
            for ($attempt = 0; $attempt -lt 450 -and !(Test-Path -LiteralPath $resultPath); $attempt++) { Start-Sleep -Milliseconds 100 }
            if (!(Test-Path -LiteralPath $resultPath)) { throw 'No update process result.' }
            $result = [IO.File]::ReadAllText($resultPath) | ConvertFrom-Json
            if ($scenario -eq 'success') {
                if (!$result.status.StartsWith('已安装更新')) { throw 'New preview did not acknowledge readiness.' }
                $receipts = @(Get-ChildItem -LiteralPath (Join-Path $directory 'updates/receipts') -Filter '*.ready')
                if ($receipts.Count -ne 1) { throw 'Missing unique child readiness receipt.' }
                $childId = [int][IO.File]::ReadAllText($receipts[0].FullName)
                $child = Get-Process -Id $childId -ErrorAction Stop
                if (!$child.Path.StartsWith($directory + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) { throw 'Unexpected child executable.' }
                Write-Output 'PASS real helper parent exit, sibling extraction, WPF child launch and readiness acknowledgement'
            } else {
                $rollbackPath = Join-Path $directory 'rollback.json'
                for ($attempt = 0; $attempt -lt 50 -and !(Test-Path -LiteralPath $rollbackPath); $attempt++) { Start-Sleep -Milliseconds 100 }
                if (!(Test-Path -LiteralPath $rollbackPath)) { throw 'Old fixture was not restarted after failed startup.' }
                $rollback = [IO.File]::ReadAllText($rollbackPath) | ConvertFrom-Json
                if ($rollback.executable -ne $old -or !$result.status.StartsWith('更新启动失败')) { throw 'Unexpected rollback target or result.' }
                Write-Output 'PASS failed WPF child startup relaunches the actual previous fixture executable'
            }
        }
        if ((Get-FileHash -LiteralPath $old -Algorithm SHA256).Hash -ne $oldHash) { throw 'Old installation was modified.' }
    } finally {
        # Only terminate fixture processes whose resolved image path is under this owned GUID root.
        foreach ($owned in @(Get-Process -Name CodexMeter -ErrorAction SilentlyContinue)) {
            try { if ($owned.Path -and $owned.Path.StartsWith($directory + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) { $owned.Kill(); $null = $owned.WaitForExit(5000) } } catch [System.InvalidOperationException] { }
        }
        $resolved = [IO.Path]::GetFullPath($directory)
        if ([IO.Path]::GetDirectoryName($resolved) -eq $fixtureRoot -and [IO.Path]::GetFileName($resolved) -match '^update-process-[a-f0-9]{32}$') { Remove-Item -LiteralPath $resolved -Recurse -Force }
    }
}
