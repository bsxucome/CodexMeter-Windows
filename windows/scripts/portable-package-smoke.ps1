param()
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.IO.Compression.FileSystem
$artifactRoot = [IO.Path]::GetFullPath((Join-Path (Split-Path $PSScriptRoot -Parent) 'artifacts/test-data'))
$fixture = Join-Path $artifactRoot ('portable-package-' + [Guid]::NewGuid().ToString('N'))
[IO.Directory]::CreateDirectory($fixture) | Out-Null
[IO.File]::WriteAllText((Join-Path $fixture '.fixture'), 'CodexMeter.PortablePackageFixture.v1')
$versionSource = [IO.File]::ReadAllText((Join-Path (Split-Path $PSScriptRoot -Parent) 'CodexMeter.Core/ProductVersion.cs'))
$version = [regex]::Match($versionSource, 'Current = "([^"]+)"').Groups[1].Value
function Reject-Package([scriptblock]$Action, [string]$Expected) {
    $rejected = $false
    try { & $Action | Out-Null } catch { if ($_.Exception.Message -ne $Expected) { throw }; $rejected = $true }
    if (!$rejected) { throw ('Expected package refusal: ' + $Expected) }
}
try {
    foreach ($runtime in @('Framework','DotNet')) {
        $inputRoot = Join-Path $fixture ("输入 $runtime")
        $outputRoot = Join-Path $fixture ("输出 $runtime")
        [IO.Directory]::CreateDirectory($inputRoot) | Out-Null
        $files = if ($runtime -eq 'Framework') { @('CodexMeter.exe','CodexMeter.Core.dll','CodexMeter.exe.config','CodexMeter.Tests.exe','settings.json') } else { @('CodexMeter.exe','CodexMeter.pdb') }
        foreach ($name in $files) { [IO.File]::WriteAllText((Join-Path $inputRoot $name), 'fixture only') }
        & (Join-Path $PSScriptRoot 'package-portable.ps1') -Runtime $runtime -InputDirectory $inputRoot -ArtifactDirectory $outputRoot
        $suffix = if ($runtime -eq 'Framework') { 'framework-x64' } else { 'dotnet10-x64' }
        $archive = Join-Path $outputRoot "CodexMeter-Windows-$version-$suffix.zip"
        $zip = [IO.Compression.ZipFile]::OpenRead($archive)
        try {
            $names = @($zip.Entries | ForEach-Object FullName)
            $root = @($names | ForEach-Object { $_.Split('/')[0] } | Sort-Object -Unique)
            $expected = @('CodexMeter.exe','使用说明.txt','docs')
            if ($runtime -eq 'Framework') { $expected += @('CodexMeter.Core.dll','CodexMeter.exe.config') }
            if (@(Compare-Object $expected $root).Count) { throw ('Wrong portable root: ' + $runtime + ': ' + ($root -join ', ')) }
            foreach ($name in @('docs/LICENSE','docs/UPSTREAM.md','docs/README.md','docs/VERIFICATION.md','docs/RELEASES.md','docs/PARITY.md','docs/ACCEPTANCE.md')) {
                if ($names -cnotcontains $name) { throw ('Missing document: ' + $name) }
            }
            if (@($names | Where-Object { $_ -match 'Tests|settings.json|\.pdb$' }).Count) { throw 'Fixture state or debug files entered the archive.' }
            $reader = New-Object IO.StreamReader($zip.GetEntry('docs/RELEASES.md').Open())
            try { $releaseGuide = $reader.ReadToEnd() } finally { $reader.Dispose() }
            if ($releaseGuide -match '\]\(releases/' -or !$releaseGuide.Contains('/blob/windows-v' + $version + '/windows/releases/')) { throw 'Portable release-note links are broken.' }
        } finally { $zip.Dispose() }
        $expectedHash = (Get-FileHash -LiteralPath $archive -Algorithm SHA256).Hash.ToLowerInvariant()
        if (![IO.File]::ReadAllText($archive + '.sha256').StartsWith($expectedHash + '  ')) { throw 'Wrong package checksum.' }
        Write-Output ('PASS fresh portable layout, complete documents, Unicode paths and excluded fixture/debug state: ' + $runtime)
        if ($runtime -eq 'DotNet') {
            $extra = Join-Path $inputRoot 'stale-runtime.dll'; [IO.File]::WriteAllText($extra, 'unbundled fixture')
            Reject-Package { & (Join-Path $PSScriptRoot 'package-portable.ps1') -Runtime DotNet -InputDirectory $inputRoot -ArtifactDirectory $outputRoot } 'Single-file publish emitted extra dependencies or content; refuse to omit them.'
            Remove-Item -LiteralPath $extra
            [IO.Directory]::CreateDirectory((Join-Path $inputRoot 'runtime')) | Out-Null
            Reject-Package { & (Join-Path $PSScriptRoot 'package-portable.ps1') -Runtime DotNet -InputDirectory $inputRoot -ArtifactDirectory $outputRoot } 'Single-file publish emitted extra dependencies or content; refuse to omit them.'
            Write-Output 'PASS unbundled DLLs and runtime directories fail closed instead of being silently dropped'
        } else {
            Remove-Item -LiteralPath (Join-Path $inputRoot 'CodexMeter.Core.dll')
            Reject-Package { & (Join-Path $PSScriptRoot 'package-portable.ps1') -Runtime Framework -InputDirectory $inputRoot -ArtifactDirectory $outputRoot } 'Missing application file: CodexMeter.Core.dll'
            Write-Output 'PASS Framework package still requires its external core library'
        }
    }
} finally {
    $resolved = [IO.Path]::GetFullPath($fixture)
    if ([IO.Path]::GetDirectoryName($resolved) -eq $artifactRoot -and [IO.Path]::GetFileName($resolved) -match '^portable-package-[a-f0-9]{32}$' -and [IO.File]::ReadAllText((Join-Path $resolved '.fixture')) -eq 'CodexMeter.PortablePackageFixture.v1') { Remove-Item -LiteralPath $resolved -Recurse -Force }
}
