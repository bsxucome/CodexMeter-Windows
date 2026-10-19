param()
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'actions-release-common.ps1')
$projectRoot = Split-Path (Split-Path $PSScriptRoot -Parent) -Parent
$operation = if ($env:BUILD_OPERATION) { $env:BUILD_OPERATION } else { 'build' }
$plan = Get-ActionsReleasePlan -ProjectRoot $projectRoot -Repository $env:GITHUB_REPOSITORY -Commit $env:GITHUB_SHA -EventName $env:GITHUB_EVENT_NAME -Reference $env:GITHUB_REF -Operation $operation
$outputs = @(('version=' + $plan.Version), ('tag=' + $plan.Tag), ('publish=' + $plan.Publish.ToString().ToLowerInvariant()))
if ($env:GITHUB_OUTPUT) { [IO.File]::AppendAllText($env:GITHUB_OUTPUT, ($outputs -join "`n") + "`n", (New-Object Text.UTF8Encoding($false))) }
Write-Output ($outputs -join "`n")
