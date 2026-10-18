param([string]$Version = '')
$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path (Split-Path $PSScriptRoot -Parent) -Parent
$toolsRoot = [IO.Path]::GetFullPath((Join-Path $projectRoot '.tools'))
$destination = [IO.Path]::GetFullPath((Join-Path $toolsRoot 'dotnet'))
if (Test-Path -LiteralPath (Join-Path $destination 'dotnet.exe')) { Write-Output "Existing local dotnet: $destination"; return }
if (Test-Path -LiteralPath $destination) { throw 'Local dotnet directory exists but is incomplete. Inspect it before retrying.' }
[IO.Directory]::CreateDirectory($toolsRoot) | Out-Null
$metadata = Invoke-RestMethod -Uri 'https://dotnetcli.blob.core.windows.net/dotnet/release-metadata/10.0/releases.json' -TimeoutSec 30
if (!$Version) { $Version = $metadata.'latest-sdk' }
if ($Version -notmatch '^10\.0\.[0-9]+$') { throw 'A stable .NET 10 SDK version is required.' }
$sdk = @($metadata.releases | ForEach-Object sdks | Where-Object { $_.version -eq $Version } | Select-Object -First 1)
if (!$sdk.Count) { throw 'SDK not listed in official release metadata.' }
$package = @($sdk[0].files | Where-Object { $_.rid -eq 'win-x64' -and $_.name -like '*.zip' } | Select-Object -First 1)
if (!$package.Count -or $package[0].hash -notmatch '^[a-fA-F0-9]{128}$') { throw 'Official SDK ZIP or SHA-512 not found.' }
$url = [Uri]$package[0].url
if ($url.Scheme -ne 'https' -or $url.Host -notin @('builds.dotnet.microsoft.com','dotnetcli.blob.core.windows.net')) { throw 'Unexpected SDK download host.' }
$archive = Join-Path $toolsRoot "dotnet-sdk-$Version-win-x64.zip"
Invoke-WebRequest -UseBasicParsing -Uri $url.AbsoluteUri -OutFile $archive -TimeoutSec 600
if ((Get-FileHash -LiteralPath $archive -Algorithm SHA512).Hash -ne $package[0].hash) { throw 'SDK SHA-512 mismatch. Nothing was installed.' }
$staging = [IO.Path]::GetFullPath((Join-Path $toolsRoot ('dotnet-setup-' + [Guid]::NewGuid().ToString('N'))))
Add-Type -AssemblyName System.IO.Compression.FileSystem
[IO.Compression.ZipFile]::ExtractToDirectory($archive, $staging)
if (!(Test-Path -LiteralPath (Join-Path $staging 'dotnet.exe'))) { throw 'SDK archive has no dotnet executable.' }
if ([IO.Path]::GetDirectoryName($staging) -ne $toolsRoot -or [IO.Path]::GetDirectoryName($destination) -ne $toolsRoot) { throw 'Unsafe SDK target path.' }
Move-Item -LiteralPath $staging -Destination $destination
& (Join-Path $destination 'dotnet.exe') --list-sdks
if ($LASTEXITCODE -ne 0) { throw 'Local SDK failed its smoke check.' }
