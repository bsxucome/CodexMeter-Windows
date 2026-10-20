param(
    [string]$TestPath = (Join-Path (Split-Path $PSScriptRoot -Parent) 'artifacts/CodexMeter-framework-x64/CodexMeter.Tests.exe'),
    [string]$BaselinePath
)
$ErrorActionPreference = 'Stop'
$artifactRoot = [IO.Path]::GetFullPath((Join-Path (Split-Path $PSScriptRoot -Parent) 'artifacts'))
$fixture = [IO.Path]::GetFullPath((Join-Path $artifactRoot ('p-' + [Guid]::NewGuid().ToString('N'))))
[IO.Directory]::CreateDirectory($fixture) | Out-Null
[IO.File]::WriteAllText((Join-Path $fixture '.fixture'), 'CodexMeter.LocatorPathFixture.v1')
try {
    $runnerRoot = 'D:\a\CodexMeter-for-Windows\CodexMeter-for-Windows'
    $cases = @(
        (Join-Path $runnerRoot 'windows/artifacts/CodexMeter-framework-x64'),
        (Join-Path $runnerRoot 'windows/CodexMeter.Tests/bin/Release/net10.0-windows')
    )
    foreach ($case in $cases) {
        $length = [Math]::Max($case.Length, $fixture.Length + 2)
        $directory = Join-Path $fixture ('r' * ($length - $fixture.Length - 1))
        [IO.Directory]::CreateDirectory($directory) | Out-Null
        $exe = Join-Path $directory 'CodexMeter.Tests.exe'
        foreach ($source in @($BaselinePath, $TestPath)) {
            if (!$source) { continue }
            Copy-Item -LiteralPath $source -Destination $exe -Force
            Copy-Item -LiteralPath (Join-Path (Split-Path $source -Parent) 'CodexMeter.Core.dll') -Destination $directory -Force
            $stdout = Join-Path $fixture 'stdout.log'
            $stderr = Join-Path $fixture 'stderr.log'
            $owned = Start-Process -FilePath $exe -ArgumentList '--locator-tests' -WindowStyle Hidden -PassThru -Wait -RedirectStandardOutput $stdout -RedirectStandardError $stderr
            try { $code = $owned.ExitCode } finally { $owned.Dispose() }
            $errorText = [IO.File]::ReadAllText($stderr)
            if ($BaselinePath -and $source -eq $BaselinePath) {
                if ($code -eq 0 -or $errorText -notmatch 'PathTooLongException') { throw 'Old locator path overflow was not reproduced.' }
                Write-Output ('PASS baseline reproduces PathTooLongException at base length ' + $length)
            } else {
                if ($code -ne 0) { throw ('Locator path regression failed: ' + $errorText) }
                Write-Output ('PASS full npm fixture at base length ' + $length + ': Unicode/space paths, native lookup, invalid path and Node fallback')
            }
        }
    }
} finally {
    if ([IO.Path]::GetDirectoryName($fixture) -eq $artifactRoot -and [IO.Path]::GetFileName($fixture) -match '^p-[a-f0-9]{32}$' -and (Test-Path -LiteralPath $fixture) -and [IO.File]::ReadAllText((Join-Path $fixture '.fixture')) -eq 'CodexMeter.LocatorPathFixture.v1') {
        Remove-Item -LiteralPath $fixture -Recurse -Force
    }
}
