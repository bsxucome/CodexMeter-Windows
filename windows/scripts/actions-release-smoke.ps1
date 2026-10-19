param()
$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path (Split-Path $PSScriptRoot -Parent) -Parent
$fixtureRoot = [IO.Path]::GetFullPath((Join-Path $projectRoot 'windows/artifacts/test-data'))
$fixture = [IO.Path]::GetFullPath((Join-Path $fixtureRoot ('actions release-' + [Guid]::NewGuid().ToString('N'))))
$names = @('PATH','GH_TOKEN','CODEXMETER_ACTIONS_FIXTURE','GITHUB_REPOSITORY','GITHUB_SHA','GITHUB_REF','GITHUB_EVENT_NAME','BUILD_OPERATION','GITHUB_OUTPUT','GITHUB_STEP_SUMMARY')
$saved = @{}
foreach ($name in $names) { $saved[$name] = [Environment]::GetEnvironmentVariable($name, 'Process') }
$gitPath = (Get-Command git -CommandType Application | Select-Object -First 1).Source
. (Join-Path $PSScriptRoot 'actions-release-common.ps1')
$realApi = (Get-Command Invoke-ActionsGithub).ScriptBlock
$actionsState = [pscustomobject]@{ Commit = ('a' * 40); Mode = ''; Calls = (New-Object Collections.Generic.List[string]); TagTarget = $null; TagWrites = 0; TransportException = $null }
function Invoke-RestMethod { throw $actionsState.TransportException }
function Reset-ActionsFixture([string]$Mode) {
    $actionsState.Mode = $Mode; $actionsState.Calls = New-Object Collections.Generic.List[string]
    $actionsState.TagTarget = $null; $actionsState.TagWrites = 0
    if ($Mode -eq 'same') { $actionsState.TagTarget = @{ type = 'commit'; sha = $actionsState.Commit } }
    if ($Mode -eq 'conflict') { $actionsState.TagTarget = @{ type = 'commit'; sha = ('c' * 40) } }
    if ($Mode -in @('annotated','loop')) { $actionsState.TagTarget = @{ type = 'tag'; sha = ('b' * 40) } }
    foreach ($name in @('created-tag','created-release')) { $path = Join-Path $fixture $name; if (Test-Path -LiteralPath $path) { Remove-Item -LiteralPath $path } }
}
function Invoke-ActionsFixtureAPI {
    param([string]$Repository, [string]$Method = 'GET', [string]$Path, $Body, [switch]$AllowNotFound)
    if ([IO.File]::ReadAllText((Join-Path $fixture '.fixture')) -ne 'CodexMeter.ActionsReleaseFixture.v1') { throw 'Invalid API fixture root.' }
    $actionsState.Calls.Add($Method + ' ' + $Path)
    if ($Method -eq 'GET' -and $Path -eq 'git/ref/tags/windows-v0.7.1') { if ($actionsState.TagTarget) { return @{ object = $actionsState.TagTarget } }; return $null }
    if ($Method -eq 'GET' -and $Path -eq 'releases/tags/windows-v0.7.1') {
        if ($actionsState.Mode -eq 'published' -or (Test-Path -LiteralPath (Join-Path $fixture 'created-release'))) {
            $assets = @('framework-x64.zip','framework-x64.zip.sha256','dotnet10-x64.zip','dotnet10-x64.zip.sha256') | Where-Object { $actionsState.Mode -ne 'missing-asset' -or $_ -ne 'dotnet10-x64.zip.sha256' } | ForEach-Object { @{ name = ('CodexMeter-Windows-0.7.1-' + $_); state = 'uploaded'; size = 128 } }
            return @{ draft = $false; tag_name = 'windows-v0.7.1'; html_url = 'https://github.com/fixture/Repo/releases/tag/windows-v0.7.1'; assets = @($assets) }
        }
        return $null
    }
    if ($Method -eq 'GET' -and $Path -eq ('git/tags/' + ('b' * 40))) { return @{ object = $(if ($actionsState.Mode -eq 'loop') { $actionsState.TagTarget } else { @{ type = 'commit'; sha = $actionsState.Commit } }) } }
    if ($Method -eq 'POST' -and $Path -eq 'git/refs') {
        if ($Body.ref -ne 'refs/tags/windows-v0.7.1' -or $Body.sha -ne $actionsState.Commit) { throw 'Tag creation used an unbuilt commit or wrong version.' }
        $actionsState.TagWrites++
        if ($actionsState.Mode -eq 'post-failed') { throw 'Fixture create failed before writing.' }
        $actionsState.TagTarget = @{ type = 'commit'; sha = $(if ($actionsState.Mode -eq 'race-conflict') { 'c' * 40 } else { $actionsState.Commit }) }
        [IO.File]::WriteAllText((Join-Path $fixture 'created-tag'), 'fixture')
        if ($actionsState.Mode -in @('lost-response','race-conflict')) { throw 'Fixture create response was lost.' }
        return @{ object = $actionsState.TagTarget }
    }
    throw ('Unexpected fixture API request: ' + $Method + ' ' + $Path)
}
function Invoke-ActionsGithub {
    param([string]$Repository, [string]$Method = 'GET', [string]$Path, $Body, [switch]$AllowNotFound)
    Invoke-ActionsFixtureAPI @PSBoundParameters
}
function Must-Reject([scriptblock]$Action, [string]$Description) {
    $rejected = $false
    try { & $Action | Out-Null } catch { $rejected = $true }
    if (!$rejected) { throw ('Expected rejection: ' + $Description) }
    Write-Output ('PASS ' + $Description)
}
try {
    foreach ($directory in @('windows/CodexMeter.Core','windows/CodexMeter.Windows','windows/scripts','packages','bin')) { [IO.Directory]::CreateDirectory((Join-Path $fixture $directory)) | Out-Null }
    [IO.File]::WriteAllText((Join-Path $fixture '.fixture'), 'CodexMeter.ActionsReleaseFixture.v1')
    [IO.File]::WriteAllText((Join-Path $fixture 'windows/CodexMeter.Core/ProductVersion.cs'), 'public const string Current = "0.7.1"; public const string Assembly = "0.7.1.0";')
    $projectPath = Join-Path $fixture 'windows/CodexMeter.Windows/CodexMeter.Windows.csproj'
    [IO.File]::WriteAllText($projectPath, '<Project><PropertyGroup><Version>0.7.1</Version></PropertyGroup></Project>')
    [IO.File]::WriteAllText((Join-Path $fixture 'windows/CodexMeter.Windows/app.manifest'), '<assembly><assemblyIdentity version="0.7.1.0" /></assembly>')
    Add-Type -TypeDefinition 'public sealed class ActionsFixtureResponse : System.Net.WebResponse { public int StatusCode { get; set; } }'
    $env:GH_TOKEN = 'fixture-token'
    foreach ($status in @(404,403,500)) {
        $response = New-Object ActionsFixtureResponse; $response.StatusCode = $status
        $actionsState.TransportException = New-Object Net.WebException('PRIVATE_SENTINEL', $null, [Net.WebExceptionStatus]::ProtocolError, $response)
        $failed = $false
        try { $value = & $realApi -Repository fixture/Repo -Path releases/tags/windows-v0.7.1 -AllowNotFound }
        catch { $failed = $true; if ($_.Exception.Message.Contains('PRIVATE_SENTINEL') -or $_.Exception.InnerException) { throw 'API error exposed transport details.' } }
        if ($failed -ne ($status -ne 404)) { throw ('Incorrect GitHub HTTP handling: ' + $status) }
    }
    [Environment]::SetEnvironmentVariable('GH_TOKEN', $saved['GH_TOKEN'], 'Process')
    Write-Output 'PASS actual HTTP wrapper treats only 404 as absent and sanitizes permission/server errors'
    $planArgs = @{ ProjectRoot = $fixture; Repository = 'fixture/Repo'; Commit = $actionsState.Commit }
    foreach ($case in @(@('push','refs/heads/main','release'), @('pull_request','refs/pull/1/merge','release'), @('workflow_dispatch','refs/heads/main','build'), @('workflow_dispatch','refs/tags/windows-v0.7.1','build'))) {
        Reset-ActionsFixture 'published'
        $plan = Get-ActionsReleasePlan @planArgs -EventName $case[0] -Reference $case[1] -Operation $case[2]
        if ($plan.Publish -or $actionsState.Calls.Count) { throw 'Build-only/PR event accessed publication APIs.' }
    }
    Write-Output 'PASS push, PR and manual build stay read-only even with release-like inputs'
    Reset-ActionsFixture 'none'
    if (!(Get-ActionsReleasePlan @planArgs -EventName workflow_dispatch -Reference refs/heads/main -Operation release).Publish -or $actionsState.TagWrites) { throw 'Manual release planning wrote a tag or was not enabled.' }
    Reset-ActionsFixture 'same'
    if (!(Get-ActionsReleasePlan @planArgs -EventName push -Reference refs/tags/windows-v0.7.1).Publish -or $actionsState.TagWrites) { throw 'Tag release plan was invalid.' }
    Write-Output 'PASS manual-main and matching tag requests plan release without writing refs'
    Must-Reject { Get-ActionsReleasePlan @planArgs -EventName workflow_dispatch -Reference refs/heads/feature -Operation release } 'manual release outside main is refused'
    Must-Reject { Get-ActionsReleasePlan @planArgs -EventName push -Reference refs/tags/windows-v0.7.2 } 'tag/product version mismatch is refused'
    Reset-ActionsFixture 'published'
    Must-Reject { Get-ActionsReleasePlan @planArgs -EventName workflow_dispatch -Reference refs/heads/main -Operation release } 'already published version is refused before building'
    Reset-ActionsFixture 'conflict'
    Must-Reject { Get-ActionsReleasePlan @planArgs -EventName workflow_dispatch -Reference refs/heads/main -Operation release } 'conflicting tag is refused before building'
    [IO.File]::WriteAllText($projectPath, '<Project><PropertyGroup><Version>0.7.2</Version></PropertyGroup></Project>')
    Must-Reject { Read-ActionsProductVersion $fixture } 'SDK/product version mismatch is refused'
    [IO.File]::WriteAllText($projectPath, '<Project><PropertyGroup><Version>0.7.1</Version></PropertyGroup></Project>')
    [IO.File]::WriteAllText((Join-Path $fixture 'windows/CodexMeter.Windows/app.manifest'), '<assembly><assemblyIdentity version="0.7.0.0" /></assembly>')
    Must-Reject { Read-ActionsProductVersion $fixture } 'manifest/product assembly version mismatch is refused'
    [IO.File]::WriteAllText((Join-Path $fixture 'windows/CodexMeter.Windows/app.manifest'), '<assembly><assemblyIdentity version="0.7.1.0" /></assembly>')
    foreach ($mode in @('none','same','annotated','lost-response','conflict','loop','race-conflict','post-failed')) {
        Reset-ActionsFixture $mode
        $failed = $false
        try { New-ActionsReleaseTag -Repository fixture/Repo -Tag windows-v0.7.1 -Commit $actionsState.Commit } catch { $failed = $true }
        if ($failed -ne ($mode -in @('conflict','loop','race-conflict','post-failed'))) { throw ('Unexpected tag creation result: ' + $mode) }
        $expectedWrites = if ($mode -in @('none','lost-response','race-conflict','post-failed')) { 1 } else { 0 }
        if ($actionsState.TagWrites -ne $expectedWrites -or $actionsState.Calls.Count -gt 11) { throw 'Tag creation overwrote a ref, retried a write or looped indefinitely.' }
        Write-Output ('PASS Actions tag creation: ' + $mode)
    }
    Reset-ActionsFixture 'same'
    [IO.File]::WriteAllText((Join-Path $fixture 'created-release'), 'fixture')
    if ((Confirm-ActionsPublishedRelease -Repository fixture/Repo -Tag windows-v0.7.1 -Commit $actionsState.Commit) -ne 'https://github.com/fixture/Repo/releases/tag/windows-v0.7.1') { throw 'Complete public release was not confirmed.' }
    $actionsState.Mode = 'missing-asset'
    Must-Reject { Confirm-ActionsPublishedRelease -Repository fixture/Repo -Tag windows-v0.7.1 -Commit $actionsState.Commit } 'incomplete uploaded assets cannot be reported as published'
    # Exercise the real output wrapper and publisher; replace only the HTTP transport.
    $commonPath = (Join-Path $PSScriptRoot 'actions-release-common.ps1').Replace("'", "''")
    $bridge = ". '$commonPath'`nfunction Invoke-ActionsGithub { param([string]`$Repository,[string]`$Method='GET',[string]`$Path,`$Body,[switch]`$AllowNotFound) Invoke-ActionsFixtureAPI @PSBoundParameters }`n"
    [IO.File]::WriteAllText((Join-Path $fixture 'windows/scripts/actions-release-common.ps1'), $bridge)
    foreach ($file in @('prepare-actions-release.ps1','publish-release.ps1')) { Copy-Item -LiteralPath (Join-Path $PSScriptRoot $file) -Destination (Join-Path $fixture ('windows/scripts/' + $file)) }
    $env:GITHUB_REPOSITORY = 'fixture/Repo'; $env:GITHUB_SHA = $actionsState.Commit; $env:GITHUB_EVENT_NAME = 'push'; $env:GITHUB_REF = 'refs/heads/main'; $env:BUILD_OPERATION = 'build'; $env:GITHUB_OUTPUT = Join-Path $fixture 'job-output.txt'
    & (Join-Path $fixture 'windows/scripts/prepare-actions-release.ps1') | Out-Null
    $outputs = [IO.File]::ReadAllLines($env:GITHUB_OUTPUT)
    if ($outputs.Count -ne 3 -or $outputs -notcontains 'version=0.7.1' -or $outputs -notcontains 'tag=windows-v0.7.1' -or $outputs -notcontains 'publish=false') { throw 'GitHub job outputs are malformed.' }
    Write-Output 'PASS workflow output file contains independent version, tag and publish values'
    $mock = @'
using System;
using System.IO;
public static class ActionsReleasePublisherFixture {
    public static int Main(string[] args) {
        string root = Environment.GetEnvironmentVariable("CODEXMETER_ACTIONS_FIXTURE");
        if (root == null || File.ReadAllText(Path.Combine(root, ".fixture")) != "CodexMeter.ActionsReleaseFixture.v1" || !File.Exists(Path.Combine(root, "created-tag"))) return 91;
        if (args.Length < 3 || args[0] != "release" || args[1] != "create" || args[2] != "windows-v0.7.1") return 92;
        File.WriteAllText(Path.Combine(root, "created-release"), "fixture"); return 0;
    }
}
'@
    Add-Type -TypeDefinition $mock -OutputAssembly (Join-Path $fixture 'bin/gh.exe') -OutputType ConsoleApplication
    $env:PATH = (Join-Path $fixture 'bin') + ';' + $saved['PATH']; $env:CODEXMETER_ACTIONS_FIXTURE = $fixture
    $env:GITHUB_STEP_SUMMARY = Join-Path $fixture 'summary.md'
    & $gitPath init --initial-branch=main $fixture | Out-Null
    & $gitPath -C $fixture add windows/CodexMeter.Core/ProductVersion.cs windows/CodexMeter.Windows/CodexMeter.Windows.csproj windows/CodexMeter.Windows/app.manifest
    & $gitPath -C $fixture -c user.name=Fixture -c user.email=fixture@example.invalid commit -m fixture | Out-Null
    if ($LASTEXITCODE -ne 0) { throw 'Local source fixture could not be committed.' }
    $actionsState.Commit = & $gitPath -C $fixture rev-parse HEAD
    foreach ($runtime in @('framework-x64','dotnet10-x64')) {
        $archive = Join-Path $fixture ("packages/CodexMeter-Windows-0.7.1-$runtime.zip")
        [IO.File]::WriteAllText($archive, 'fixture package')
        [IO.File]::WriteAllText($archive + '.sha256', (Get-FileHash -LiteralPath $archive -Algorithm SHA256).Hash.ToLowerInvariant() + '  ' + [IO.Path]::GetFileName($archive))
    }
    Push-Location -LiteralPath $fixture
    try {
        $publisher = Join-Path $fixture 'windows/scripts/publish-release.ps1'
        $publishArgs = @{ Repository = 'fixture/Repo'; Tag = 'windows-v0.7.1'; Directory = (Join-Path $fixture 'packages'); CreateTag = $true; Commit = $actionsState.Commit }
        Reset-ActionsFixture 'none'
        & $publisher @publishArgs | Out-Null
        if ($actionsState.TagWrites -ne 1 -or !(Test-Path -LiteralPath (Join-Path $fixture 'created-release'))) { throw 'Verified packages did not reach tag and Release publication.' }
        if (![IO.File]::ReadAllText($env:GITHUB_STEP_SUMMARY).Contains('https://github.com/fixture/Repo/releases/tag/windows-v0.7.1')) { throw 'Actions summary omitted the verified Release URL.' }
        Reset-ActionsFixture 'none'
        $publishArgs.Commit = 'f' * 40
        Must-Reject { & $publisher @publishArgs } 'wrong checkout commit cannot create a tag or Release'
        if ($actionsState.TagWrites) { throw 'Wrong source created a tag.' }
        $publishArgs.Commit = $actionsState.Commit
        [IO.File]::AppendAllText((Join-Path $fixture 'packages/CodexMeter-Windows-0.7.1-dotnet10-x64.zip'), 'tampered')
        Must-Reject { & $publisher @publishArgs } 'package tampering is rejected before tag creation'
        if ($actionsState.TagWrites) { throw 'Tampered package created a tag.' }
    } finally { Pop-Location }
    Write-Output 'PASS one-run publisher verifies packages and source before creating a tag and Release'
} finally {
    foreach ($name in $names) { [Environment]::SetEnvironmentVariable($name, $saved[$name], 'Process') }
    if ([IO.Path]::GetDirectoryName($fixture) -eq $fixtureRoot -and [IO.Path]::GetFileName($fixture) -match '^actions release-[a-f0-9]{32}$' -and (Test-Path -LiteralPath $fixture)) { Remove-Item -LiteralPath $fixture -Recurse -Force }
}
