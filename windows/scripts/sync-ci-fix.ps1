param()
$ErrorActionPreference = 'Stop'
$projectRoot = [IO.Path]::GetFullPath((Split-Path (Split-Path $PSScriptRoot -Parent) -Parent))
$gitPath = (Get-Command git -CommandType Application -ErrorAction Stop | Select-Object -First 1).Source
$gitArguments = @('-c', ('safe.directory=' + $projectRoot.Replace('\','/')), '-C', $projectRoot)
$remote = & $gitPath @gitArguments remote get-url --push origin
if ($LASTEXITCODE -ne 0 -or $remote -ne 'https://github.com/bsxucome/CodexMeter-Windows.git') { throw 'Origin does not match the authorized GitHub repository. Nothing was pushed.' }
$branch = & $gitPath @gitArguments branch --show-current
if ($LASTEXITCODE -ne 0 -or $branch -ne 'main') { throw 'Expected the main branch. Nothing was pushed.' }
$commit = & $gitPath @gitArguments rev-parse HEAD
if ($LASTEXITCODE -ne 0 -or $commit -notmatch '^[a-f0-9]{40}$') { throw 'Could not identify the local commit.' }
Write-Output ('Pushing committed main: ' + $commit)
# Keep the normal console attached, matching the user's successful direct push.
# This command pushes existing commits only; it never stages, commits or forces.
try {
    $ErrorActionPreference = 'Continue'
    & $gitPath @gitArguments push origin main
    $pushResult = $LASTEXITCODE
} finally { $ErrorActionPreference = 'Stop' }
if ($pushResult -ne 0) { throw ('Git push failed with exit code ' + $pushResult + '. The original Git error is displayed above.') }
& (Join-Path $PSScriptRoot 'collect-ci-logs.ps1') -ExpectedCommit $commit -WaitForCompletion
