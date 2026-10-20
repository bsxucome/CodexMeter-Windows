param()
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'actions-release-common.ps1')
$projectRoot = Split-Path (Split-Path $PSScriptRoot -Parent) -Parent
$version = Read-ActionsProductVersion $projectRoot
if ($env:RELEASE_TAG -ne ('windows-v' + $version)) { throw 'Release tag does not match product version.' }
if ($env:BUILD_RUNTIME -notin @('Framework','DotNet')) { throw 'Invalid build runtime.' }
if ($env:REPOSITORY -notmatch '^[A-Za-z0-9_.-]+/[A-Za-z0-9_.-]+$' -or @($env:REPOSITORY.Split('/') | Where-Object { $_ -in @('.','..') }).Count) { throw 'Invalid release repository.' }
if (!$env:CODEXMETER_RELEASE_KEY_XML) { Write-Output '未配置更新签名密钥，仅发布便携包。'; return }
$artifactRoot = Join-Path $projectRoot 'windows/artifacts'
$runtime = if ($env:BUILD_RUNTIME -eq 'Framework') { 'framework-x64' } else { 'dotnet10-x64' }
$channel = if ($version.Contains('-')) { 'beta' } else { 'stable' }
$name = "CodexMeter-Windows-$version-$runtime.zip"
$feed = Join-Path $artifactRoot 'feed'
& (Join-Path $PSScriptRoot 'sign-release.ps1') -Archive (Join-Path $artifactRoot $name) -Version $version -Channel $channel -Runtime $runtime -PackageUrl "https://github.com/$env:REPOSITORY/releases/download/$env:RELEASE_TAG/$name" -OutputDirectory $feed
Copy-Item -LiteralPath (Join-Path $feed "$channel-$runtime.json") -Destination $artifactRoot
Copy-Item -LiteralPath (Join-Path $feed 'public-key.json') -Destination (Join-Path $artifactRoot "public-key-$runtime.json")
