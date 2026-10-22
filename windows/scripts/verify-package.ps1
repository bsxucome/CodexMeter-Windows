param([string]$Archive, [ValidateSet('Framework','DotNet')][string]$Runtime = 'Framework')
$ErrorActionPreference = 'Stop'
$windowsRoot = Split-Path $PSScriptRoot -Parent
$source = [IO.File]::ReadAllText((Join-Path $windowsRoot 'CodexMeter.Core/ProductVersion.cs'))
$releaseVersion = [regex]::Match($source, 'Current = "([^"]+)"').Groups[1].Value
$assemblyVersion = [regex]::Match($source, 'Assembly = "([^"]+)"').Groups[1].Value
if (!$releaseVersion -or !$assemblyVersion) { throw 'Missing product version.' }
$runtimeName = if ($Runtime -eq 'Framework') { 'framework-x64' } else { 'dotnet10-x64' }
if (!$Archive) { $Archive = Join-Path $windowsRoot "artifacts/CodexMeter-Windows-$releaseVersion-$runtimeName.zip" }
Add-Type -AssemblyName System.IO.Compression.FileSystem
$archivePath = (Resolve-Path -LiteralPath $Archive).Path
$artifactRoot = [IO.Path]::GetFullPath((Join-Path (Split-Path $PSScriptRoot -Parent) 'artifacts'))
$directory = [IO.Path]::GetFullPath((Join-Path $artifactRoot ('portable check 中文 ' + [Guid]::NewGuid().ToString('N'))))
if ([IO.Path]::GetDirectoryName($directory) -ne $artifactRoot -or (Test-Path -LiteralPath $directory)) { throw 'Extraction requires a new directory directly under artifacts.' }
$hash = (Get-FileHash -LiteralPath $archivePath -Algorithm SHA256).Hash.ToLowerInvariant()
if (([IO.File]::ReadAllText($archivePath + '.sha256').Split(' ')[0]) -ne $hash) { throw 'Package hash mismatch.' }
$zip = [IO.Compression.ZipFile]::OpenRead($archivePath)
try {
    $names = @($zip.Entries | ForEach-Object FullName)
    foreach ($name in $names) {
        if ($name -match '(^|/)(test-data|screenshots)(/|$)|Tests|private|\.pfx$|\.p12$|^/|[\\:]|(^|/)\.\.(/|$)' -or $name -in @('settings.json','remote-secrets.bin','history.sqlite3')) { throw 'Unexpected package contents.' }
    }
    $required = @('CodexMeter.exe','使用说明.txt','docs/README.md','docs/LICENSE','docs/UPSTREAM.md','docs/VERIFICATION.md','docs/PARITY.md','docs/RELEASES.md','docs/ACCEPTANCE.md')
    if ($Runtime -eq 'Framework') { $required += @('CodexMeter.Core.dll','CodexMeter.exe.config') }
    foreach ($name in $required) {
        if ($names -notcontains $name) { throw "Required package file missing: $name" }
    }
    $files = @($names | Where-Object { !$_.EndsWith('/') })
    if ($files.Count -ne $required.Count -or @($files | Where-Object { $_ -cnotin $required }).Count) { throw 'Unexpected portable package layout.' }
    if (@($names | Where-Object { $_.EndsWith('/') -and $_ -cne 'docs/' }).Count) { throw 'Unexpected portable package directory.' }
    Write-Output ('PASS package file selection: ' + $names.Count + ' files')
} finally { $zip.Dispose() }
[IO.Compression.ZipFile]::ExtractToDirectory($archivePath, $directory)
$exe = Join-Path $directory 'CodexMeter.exe'
$version = [Diagnostics.FileVersionInfo]::GetVersionInfo($exe).FileVersion
if ($version -ne $assemblyVersion) { throw 'Unexpected application version.' }
foreach ($mode in @('main','settings','appearance','history','about')) {
    $arguments = @('--smoke'); if ($mode -ne 'main') { $arguments += '--' + $mode }
    $owned = Start-Process -FilePath $exe -ArgumentList $arguments -WindowStyle Hidden -PassThru
    try {
        if (!$owned.WaitForExit(30000) -or $owned.ExitCode -ne 0) { throw ('Extracted portable preview failed: ' + $mode) }
        $image = if ($mode -eq 'main') { 'light.png' } else { $mode + '-light.png' }
        if (!(Test-Path -LiteralPath (Join-Path $directory ('screenshots/' + $image)))) { throw ('Preview produced no window screenshot: ' + $mode) }
    } finally { $owned.Refresh(); if (!$owned.HasExited) { $owned.Kill(); if (!$owned.WaitForExit(5000)) { throw 'Owned package preview did not exit.' } }; $owned.Dispose() }
    Write-Output ('PASS extracted portable preview: ' + $mode)
}
Write-Output "PASS extracted portable Unicode/space path, version $version"
Write-Output "SHA256: $hash"
Write-Output "Extracted: $directory"
