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
# Preserve stdin for Git Credential Manager and display native progress/errors.
# Capture only a bounded diagnostic tail; PS5 native stderr must not terminate
# before its exit code can be checked. Push the inspected snapshot without force.
$pushResult = -1
for ($attempt = 0; $attempt -lt 2; $attempt++) {
    $diagnostics = New-Object 'System.Collections.Generic.Queue[string]'
    $transportArguments = @('-c', 'http.sslVerify=true')
    if ($attempt -eq 1) {
        Write-Output 'Schannel handshake failed. Retrying once with command-local OpenSSL and HTTP/1.1; certificate verification stays enabled.'
        $transportArguments += @('-c', 'http.sslBackend=openssl', '-c', 'http.version=HTTP/1.1')
    }
    $previousPreference = $ErrorActionPreference
    try {
        $ErrorActionPreference = 'Continue'
        & $gitPath @gitArguments @transportArguments push origin ($commit + ':refs/heads/main') 2>&1 | ForEach-Object {
            $line = $_.ToString()
            Write-Host $line
            if ($diagnostics.Count -ge 64) { $null = $diagnostics.Dequeue() }
            $diagnostics.Enqueue($line.Substring(0, [Math]::Min($line.Length, 2048)))
        }
        $pushResult = $LASTEXITCODE
    } finally { $ErrorActionPreference = $previousPreference }
    if ($pushResult -eq 0) { break }
    $errorTail = $diagnostics -join "`n"
    $handshakeFailure = $errorTail -match '(?im)schannel:.*(failed to receive handshake|SSL/TLS connection failed)'
    $permanentFailure = $errorTail -match '(?i)certificate|cert_|SEC_E_UNTRUSTED_ROOT|SEC_E_WRONG_PRINCIPAL|revocation|authentication failed|permission denied|access denied|repository not found|\[rejected\]|\[remote rejected\]|non-fast-forward|unsupported SSL backend'
    if ($attempt -ne 0 -or !$handshakeFailure -or $permanentFailure) { break }
}
if ($pushResult -ne 0) { throw ('Git push failed with exit code ' + $pushResult + '. The Git error is displayed above. No successful push was confirmed; CI status was not collected.') }
& (Join-Path $PSScriptRoot 'collect-ci-logs.ps1') -ExpectedCommit $commit -WaitForCompletion
