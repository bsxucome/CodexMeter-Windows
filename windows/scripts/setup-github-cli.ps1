param()
$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path (Split-Path $PSScriptRoot -Parent) -Parent
$toolsRoot = [IO.Path]::GetFullPath((Join-Path $projectRoot '.tools'))
$existing = Get-Command gh -ErrorAction SilentlyContinue
if ($existing) { Write-Output $existing.Source; return }
[IO.Directory]::CreateDirectory($toolsRoot) | Out-Null
$headers = @{ 'User-Agent'='CodexMeter-Windows-publisher'; Accept='application/vnd.github+json' }
$release = Invoke-RestMethod -Uri 'https://api.github.com/repos/cli/cli/releases/latest' -Headers $headers -TimeoutSec 20
$package = @($release.assets | Where-Object { $_.name -match '^gh_[0-9.]+_windows_amd64\.zip$' } | Select-Object -First 1)
$checksum = @($release.assets | Where-Object { $_.name -match '^gh_[0-9.]+_checksums\.txt$' } | Select-Object -First 1)
if (!$package.Count -or !$checksum.Count) { throw 'Official GitHub CLI assets not found.' }
foreach ($asset in @($package[0],$checksum[0])) {
    $url = [Uri]$asset.browser_download_url
    if ($url.Scheme -ne 'https' -or $url.Host -ne 'github.com' -or !$url.AbsolutePath.StartsWith('/cli/cli/releases/download/')) { throw 'Unexpected GitHub CLI asset URL.' }
}
$archive = Join-Path $toolsRoot $package[0].name
$checksumPath = Join-Path $toolsRoot $checksum[0].name
Invoke-WebRequest -UseBasicParsing -Uri $package[0].browser_download_url -OutFile $archive -TimeoutSec 180
Invoke-WebRequest -UseBasicParsing -Uri $checksum[0].browser_download_url -OutFile $checksumPath -TimeoutSec 30
$expected = @([IO.File]::ReadAllLines($checksumPath) | Where-Object { $_ -match ('^[a-fA-F0-9]{64}\s+\*?' + [regex]::Escape($package[0].name) + '$') })
if ($expected.Count -ne 1 -or (Get-FileHash -LiteralPath $archive -Algorithm SHA256).Hash -ne $expected[0].Substring(0,64)) { throw 'GitHub CLI checksum mismatch.' }
$destination = Join-Path $toolsRoot ('github-cli-' + [Guid]::NewGuid().ToString('N'))
Add-Type -AssemblyName System.IO.Compression.FileSystem
[IO.Compression.ZipFile]::ExtractToDirectory($archive, $destination)
$executable = Join-Path $destination 'bin/gh.exe'
if (!(Test-Path -LiteralPath $executable)) { throw 'GitHub CLI executable not found in official archive.' }
& $executable --version | Out-Null
if ($LASTEXITCODE -ne 0) { throw 'GitHub CLI smoke check failed.' }
Write-Output $executable
