param(
    [ValidatePattern('^[A-Za-z0-9_.-]+/[A-Za-z0-9_.-]+$')][string]$Repository = 'bsxucome/CodexMeter-for-Windows',
    [ValidatePattern('^[a-fA-F0-9]{40}$')][string]$ExpectedCommit,
    [ValidatePattern('^(main|windows-v[0-9]+\.[0-9]+\.[0-9]+(-[0-9A-Za-z.-]+)?)$')][string]$Reference = 'main',
    [switch]$WaitForCompletion
)
$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path (Split-Path $PSScriptRoot -Parent) -Parent
$destination = Join-Path $projectRoot 'windows/artifacts/ci-logs'
[IO.Directory]::CreateDirectory($destination) | Out-Null
$command = Get-Command gh -CommandType Application -ErrorAction SilentlyContinue | Select-Object -First 1
$ghPath = if ($command) { $command.Source } else { $null }
if (!$ghPath) {
    $cache = Join-Path $projectRoot '.tools'
    if (Test-Path -LiteralPath $cache) {
        $ghPath = @(Get-ChildItem -LiteralPath $cache -Directory -Filter 'github-cli-*' | Sort-Object LastWriteTime -Descending | ForEach-Object { Join-Path $_.FullName 'bin/gh.exe' } | Where-Object { Test-Path -LiteralPath $_ } | Select-Object -First 1)[0]
    }
}
if (!$ghPath) { $ghPath = & (Join-Path $PSScriptRoot 'setup-github-cli.ps1') }
if (!$ghPath -or !(Test-Path -LiteralPath $ghPath)) { throw 'GitHub CLI is unavailable.' }
function Read-GitHub([string[]]$Arguments, [string]$OutputName) {
    # These read-only arguments contain only fixed flags, a validated repo and
    # a numeric run ID. Native redirection avoids PS5 NativeCommandError.
    $outputPath = Join-Path $destination $OutputName
    $errorPath = Join-Path $destination ($OutputName + '.stderr')
    $process = Start-Process -FilePath $ghPath -ArgumentList $Arguments -WorkingDirectory $projectRoot -WindowStyle Hidden -PassThru -Wait -RedirectStandardOutput $outputPath -RedirectStandardError $errorPath
    try {
        if ($process.ExitCode -ne 0) { throw ('GitHub log request failed. See ' + $errorPath) }
    } finally { $process.Dispose() }
    return [IO.File]::ReadAllText($outputPath)
}
$deadline = [DateTimeOffset]::UtcNow.AddMinutes(35)
do {
    $runsJson = Read-GitHub -Arguments @('run','list','--repo',$Repository,'--workflow','windows.yml','--branch',$Reference,'--limit','20','--json','databaseId,headSha,headBranch,status,conclusion,url,workflowName') -OutputName 'runs.json'
    $runs = ConvertFrom-Json -InputObject $runsJson
    $selected = @($runs | Where-Object { (!$ExpectedCommit -or $_.headSha -eq $ExpectedCommit) -and ($_.headBranch -eq $Reference -or (!$_.headBranch -and $Reference -eq 'main')) } | Select-Object -First 1)
    $run = if ($selected.Count) { $selected[0] } else { $null }
    if (!$WaitForCompletion -or ($run -and $run.status -eq 'completed') -or [DateTimeOffset]::UtcNow -ge $deadline) { break }
    $state = if ($run) { $run.status } else { 'waiting for the pushed commit to appear' }
    Write-Output ('CI: ' + $state + '; checking again in 10 seconds...')
    Start-Sleep -Seconds 10
} while ($true)
$summaryPath = Join-Path $destination 'latest-result.txt'
if (!$run) {
    $message = 'No matching ' + $Reference + ' run found.' + $(if ($ExpectedCommit) { ' Expected commit: ' + $ExpectedCommit } else { '' })
    [IO.File]::WriteAllText($summaryPath, $message)
    [IO.File]::WriteAllText((Join-Path $destination 'latest-result.json'), (@{ repository = $Repository; reference = $Reference; commit = $ExpectedCommit; status = 'not_found'; conclusion = ''; url = '' } | ConvertTo-Json))
    Write-Output $message
    return
}
$runId = $run.databaseId.ToString()
if ($runId -notmatch '^[0-9]+$') { throw 'GitHub returned an invalid run ID.' }
$log = if ($run.conclusion -eq 'failure') { Read-GitHub -Arguments @('run','view',$runId,'--repo',$Repository,'--log-failed') -OutputName ('run-' + $runId + '.log') } else { '' }
$summary = 'Repository: ' + $Repository + "`r`nRun: " + $run.url + "`r`nCommit: " + $run.headSha + "`r`nStatus: " + $run.status + "`r`nConclusion: " + $run.conclusion + "`r`n`r`n" + $log
# Keep the handoff local and exclude common credential forms from the summary.
$summary = [regex]::Replace($summary, '(?i)(https?://)[^/\s@]+@', '$1[redacted]@')
$summary = [regex]::Replace($summary, 'github_pat_[A-Za-z0-9_]+|gh[pousr]_[A-Za-z0-9]{20,}', '[redacted]')
[IO.File]::WriteAllText($summaryPath, $summary, (New-Object Text.UTF8Encoding($true)))
[IO.File]::WriteAllText((Join-Path $destination 'latest-result.json'), (@{ repository = $Repository; reference = $Reference; commit = $run.headSha; status = $run.status; conclusion = $run.conclusion; url = $run.url; runId = $runId } | ConvertTo-Json))
if ($run.conclusion -eq 'failure') { [IO.File]::WriteAllText((Join-Path $destination 'latest-failure.txt'), $summary, (New-Object Text.UTF8Encoding($true))) }
Write-Output ('Run ' + $runId + ': ' + $run.status + ' / ' + $run.conclusion)
Write-Output ('Saved: ' + $summaryPath)
