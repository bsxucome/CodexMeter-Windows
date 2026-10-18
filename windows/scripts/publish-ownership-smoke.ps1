param()
$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path (Split-Path $PSScriptRoot -Parent) -Parent
$fixtureRoot = [IO.Path]::GetFullPath((Join-Path $projectRoot 'windows/artifacts/test-data'))
$fixture = [IO.Path]::GetFullPath((Join-Path $fixtureRoot ('publish ownership-' + [Guid]::NewGuid().ToString('N'))))
$gitPath = (Get-Command git -CommandType Application).Source
$names = @('GIT_TEST_ASSUME_DIFFERENT_OWNER','GIT_CONFIG_GLOBAL','GIT_CONFIG_NOSYSTEM','GIT_CONFIG_PARAMETERS','GIT_CONFIG_COUNT','GIT_CONFIG_KEY_0','GIT_CONFIG_VALUE_0','GIT_CONFIG_KEY_1','GIT_CONFIG_VALUE_1')
$saved = @{}
foreach ($name in $names) { $saved[$name] = [Environment]::GetEnvironmentVariable($name, 'Process') }
function Assert-OwnershipRejected([string]$directory) {
    $log = Join-Path $fixture 'windows/artifacts/rejected.log'
    $output = Join-Path $fixture 'windows/artifacts/rejected-output.log'
    $probe = Start-Process -FilePath $gitPath -ArgumentList 'status','--short' -WorkingDirectory $directory -WindowStyle Hidden -Wait -PassThru -RedirectStandardError $log -RedirectStandardOutput $output
    if ($probe.ExitCode -eq 0 -or [IO.File]::ReadAllText($log) -notmatch 'dubious ownership') { throw 'Git ownership rejection was not reproduced.' }
}
try {
    foreach ($directory in @('.github','windows/scripts','windows/artifacts')) { [IO.Directory]::CreateDirectory((Join-Path $fixture $directory)) | Out-Null }
    foreach ($file in @('.gitattributes','AGENTS.md','CONTRIBUTING.md','LICENSE','README.md','UPSTREAM.md','.github/fixture.txt')) { [IO.File]::WriteAllText((Join-Path $fixture $file), "Ownership fixture`n") }
    [IO.File]::WriteAllText((Join-Path $fixture '.gitignore'), "windows/artifacts/`n")
    Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'publish-github.ps1') -Destination (Join-Path $fixture 'windows/scripts/publish-github.ps1')
    $globalFile = Join-Path $fixture 'windows/artifacts/isolated-global.gitconfig'
    [IO.File]::WriteAllText($globalFile, "[user]`n name = Ownership Fixture`n")
    [Environment]::SetEnvironmentVariable('GIT_CONFIG_GLOBAL', $globalFile, 'Process')
    [Environment]::SetEnvironmentVariable('GIT_CONFIG_NOSYSTEM', '1', 'Process')
    [Environment]::SetEnvironmentVariable('GIT_CONFIG_PARAMETERS', $null, 'Process')
    [Environment]::SetEnvironmentVariable('GIT_CONFIG_COUNT', '0', 'Process')
    [Environment]::SetEnvironmentVariable('GIT_TEST_ASSUME_DIFFERENT_OWNER', $null, 'Process')
    & $gitPath init --initial-branch=main $fixture | Out-Null
    if ($LASTEXITCODE -ne 0) { throw 'Fixture initialization failed.' }
    $globalHash = (Get-FileHash -LiteralPath $globalFile -Algorithm SHA256).Hash
    [Environment]::SetEnvironmentVariable('GIT_TEST_ASSUME_DIFFERENT_OWNER', '1', 'Process')
    Assert-OwnershipRejected $fixture
    foreach ($count in @(0,1)) {
        [Environment]::SetEnvironmentVariable('GIT_CONFIG_COUNT', $count.ToString(), 'Process')
        [Environment]::SetEnvironmentVariable('GIT_CONFIG_KEY_0', 'fixture.keep', 'Process')
        [Environment]::SetEnvironmentVariable('GIT_CONFIG_VALUE_0', 'existing-value', 'Process')
        [Environment]::SetEnvironmentVariable('GIT_CONFIG_KEY_1', 'fixture.unused', 'Process')
        [Environment]::SetEnvironmentVariable('GIT_CONFIG_VALUE_1', 'unused-value', 'Process')
        $before = @{}
        foreach ($name in $names) { $before[$name] = [Environment]::GetEnvironmentVariable($name, 'Process') }
        & (Join-Path $fixture 'windows/scripts/publish-github.ps1') -PrepareOnly | Out-Null
        if (!(Test-Path -LiteralPath (Join-Path $fixture 'windows/artifacts/CodexMeter-Windows-source.zip'))) { throw 'Source archive was not prepared.' }
        foreach ($name in $names) { if ([Environment]::GetEnvironmentVariable($name, 'Process') -cne $before[$name]) { throw ('Configuration leaked after publishing: ' + $name) } }
        Assert-OwnershipRejected $fixture
        if ((Get-FileHash -LiteralPath $globalFile -Algorithm SHA256).Hash -ne $globalHash) { throw 'Global Git configuration was changed.' }
        Write-Output "PASS source preparation under simulated foreign ownership; existing config count=$count preserved, scope restored, global config unchanged"
    }
    [IO.File]::WriteAllText((Join-Path $fixture 'windows/settings.json'), '{}')
    $refused = $false
    try { & (Join-Path $fixture 'windows/scripts/publish-github.ps1') -PrepareOnly | Out-Null }
    catch { if ($_.Exception.Message -notlike '*Local data or private files*') { throw }; $refused = $true }
    if (!$refused) { throw 'Forbidden fixture settings were not refused.' }
    foreach ($name in $names) { if ([Environment]::GetEnvironmentVariable($name, 'Process') -cne $before[$name]) { throw ('Configuration leaked on failure: ' + $name) } }
    Assert-OwnershipRejected $fixture
    if ((Get-FileHash -LiteralPath $globalFile -Algorithm SHA256).Hash -ne $globalHash) { throw 'Failure changed global Git configuration.' }
    Write-Output 'PASS failed publication restores configuration and retains the user-data staging guard'
} finally {
    foreach ($name in $names) { [Environment]::SetEnvironmentVariable($name, $saved[$name], 'Process') }
    if ([IO.Path]::GetDirectoryName($fixture) -eq $fixtureRoot -and [IO.Path]::GetFileName($fixture) -match '^publish ownership-[a-f0-9]{32}$' -and (Test-Path -LiteralPath $fixture)) { Remove-Item -LiteralPath $fixture -Recurse -Force }
}
