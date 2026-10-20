param(
    [string]$AppPath = (Join-Path (Split-Path $PSScriptRoot -Parent) 'artifacts/CodexMeter-framework-x64/CodexMeter.exe'),
    [string]$BaselinePath
)
$ErrorActionPreference = 'Stop'
$windowsRoot = Split-Path $PSScriptRoot -Parent
$fixtureRoot = [IO.Path]::GetFullPath((Join-Path $windowsRoot 'artifacts/test-data'))
$fixture = [IO.Path]::GetFullPath((Join-Path $fixtureRoot ('settings scroll-' + [Guid]::NewGuid().ToString('N'))))
[IO.Directory]::CreateDirectory($fixture) | Out-Null
[IO.File]::WriteAllText((Join-Path $fixture '.fixture'), 'CodexMeter.SettingsScrollFixture.v1')
try {
    $framework = Join-Path $env:WINDIR 'Microsoft.NET/Framework64/v4.0.30319'
    $wpf = Join-Path $framework 'WPF'
    $core = Join-Path (Split-Path $AppPath -Parent) 'CodexMeter.Core.dll'
    Copy-Item -LiteralPath $core -Destination $fixture
    $probe = Join-Path $fixture 'SettingsScroll.Tests.exe'
    & (Join-Path $framework 'csc.exe') /nologo /codepage:65001 /langversion:5 /warnaserror+ /platform:x64 /define:SETTINGS_SCROLL_TEST /target:exe "/out:$probe" "/r:$core" /r:System.Xaml.dll "/r:$wpf/PresentationCore.dll" "/r:$wpf/PresentationFramework.dll" "/r:$wpf/WindowsBase.dll" "/r:$wpf/UIAutomationTypes.dll" (Join-Path $windowsRoot 'CodexMeter.Tests/SettingsScrollProbe.cs')
    if ($LASTEXITCODE -ne 0) { throw 'Settings scroll probe build failed.' }
    if ($BaselinePath) {
        & $probe ([IO.Path]::GetFullPath($BaselinePath)) --expect-trap
        if ($LASTEXITCODE -ne 0) { throw 'Old settings wheel trap was not reproduced.' }
    }
    & $probe ([IO.Path]::GetFullPath($AppPath))
    if ($LASTEXITCODE -ne 0) { throw 'Settings scrolling regression failed.' }
} finally {
    if ([IO.Path]::GetDirectoryName($fixture) -eq $fixtureRoot -and [IO.Path]::GetFileName($fixture) -match '^settings scroll-[a-f0-9]{32}$' -and (Test-Path -LiteralPath $fixture) -and [IO.File]::ReadAllText((Join-Path $fixture '.fixture')) -eq 'CodexMeter.SettingsScrollFixture.v1') { Remove-Item -LiteralPath $fixture -Recurse -Force }
}
