param([ValidatePattern('^[A-Za-z0-9][A-Za-z0-9._/-]{0,120}$')][string]$Reference = 'main')
$ErrorActionPreference = 'Stop'
if ($Reference.Contains('..')) { throw 'Invalid upstream reference.' }
$projectRoot = Split-Path (Split-Path $PSScriptRoot -Parent) -Parent
$toolsRoot = Join-Path $projectRoot '.tools'
[IO.Directory]::CreateDirectory($toolsRoot) | Out-Null
$checkout = Join-Path $toolsRoot ('upstream-' + [Guid]::NewGuid().ToString('N'))
git init $checkout
if ($LASTEXITCODE -ne 0) { throw 'Upstream checkout initialization failed.' }
git -C $checkout remote add origin https://github.com/raycalrui/CodexMeter.git
git -C $checkout fetch --depth=1 origin $Reference
if ($LASTEXITCODE -ne 0) { throw 'Upstream fetch failed. No reviewed commit was recorded.' }
git -C $checkout checkout --detach FETCH_HEAD
if ($LASTEXITCODE -ne 0) { throw 'Upstream checkout failed.' }
$commit = (git -C $checkout rev-parse HEAD).Trim()
if ($commit -notmatch '^[a-f0-9]{40}$') { throw 'Upstream commit could not be verified.' }
$wanted = @('README.md','AGENTS.md','LICENSE','CodexMeter/ContentView.swift','CodexMeter/MenuBarProgressView.swift','CodexMeter/AppSettings.swift','CodexMeter/NotificationManager.swift')
$files = @()
foreach ($name in $wanted) {
    $path = Join-Path $checkout $name
    if (Test-Path -LiteralPath $path) { $files += @{ path=$name; sha256=(Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash.ToLowerInvariant() } }
}
$record = @{ repository='https://github.com/raycalrui/CodexMeter.git'; reference=$Reference; commit=$commit; retrievedAt=[DateTimeOffset]::UtcNow.ToString('O'); files=$files; status='retrieved-for-review' } | ConvertTo-Json -Depth 5
[IO.File]::WriteAllText((Join-Path $projectRoot 'windows/upstream-reference.json'), $record, (New-Object Text.UTF8Encoding($false)))
Write-Output "Verified upstream commit: $commit"
Write-Output "Read the fetched AGENTS.md before comparing or copying sources: $checkout"
Write-Output 'Retrieval is recorded separately from a completed parity review.'
