param([string]$Archive = (Join-Path (Split-Path $PSScriptRoot -Parent) 'artifacts/CodexMeter-Windows-0.7.0-framework-x64.zip'))
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.IO.Compression.FileSystem
$archivePath = (Resolve-Path -LiteralPath $Archive).Path
$artifactRoot = [IO.Path]::GetFullPath((Join-Path (Split-Path $PSScriptRoot -Parent) 'artifacts'))
$directory = [IO.Path]::GetFullPath((Join-Path $artifactRoot ([IO.Path]::GetFileNameWithoutExtension($archivePath))))
if ([IO.Path]::GetDirectoryName($directory) -ne $artifactRoot -or (Test-Path -LiteralPath $directory)) { throw 'Extraction requires a new directory directly under artifacts.' }
$hash = (Get-FileHash -LiteralPath $archivePath -Algorithm SHA256).Hash.ToLowerInvariant()
if (([IO.File]::ReadAllText($archivePath + '.sha256').Split(' ')[0]) -ne $hash) { throw 'Package hash mismatch.' }
$zip = [IO.Compression.ZipFile]::OpenRead($archivePath)
try {
    $names = @($zip.Entries | ForEach-Object FullName)
    foreach ($name in $names) {
        if ($name -match '(^|/)(test-data|screenshots)(/|$)|Tests|private|\.pfx$|\.p12$|^/|[\\:]|(^|/)\.\.(/|$)' -or $name -in @('settings.json','remote-secrets.bin','history.sqlite3')) { throw 'Unexpected package contents.' }
    }
    foreach ($name in @('CodexMeter.exe','CodexMeter.Core.dll','README.md','LICENSE','UPSTREAM.md','VERIFICATION.md','PARITY.md','RELEASES.md','ACCEPTANCE.md')) {
        if ($names -notcontains $name) { throw "Required package file missing: $name" }
    }
    Write-Output ('PASS package file selection: ' + $names.Count + ' files')
} finally { $zip.Dispose() }
[IO.Compression.ZipFile]::ExtractToDirectory($archivePath, $directory)
$exe = Join-Path $directory 'CodexMeter.exe'
$version = [Diagnostics.FileVersionInfo]::GetVersionInfo($exe).FileVersion
if ($version -ne '0.7.0.0') { throw 'Unexpected application version.' }
$owned = Start-Process -FilePath $exe -ArgumentList '--smoke','--settings' -WindowStyle Hidden -PassThru
try {
    if (!$owned.WaitForExit(15000) -or $owned.ExitCode -ne 0) { throw 'Extracted portable preview failed.' }
} finally { $owned.Refresh(); if (!$owned.HasExited) { $owned.Kill() } }
Write-Output "PASS extracted portable settings preview, version $version"
Write-Output "SHA256: $hash"
Write-Output "Extracted: $directory"
