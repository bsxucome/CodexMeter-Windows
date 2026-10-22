param(
    [Parameter(Mandatory=$true)][ValidateSet('Framework','DotNet')][string]$Runtime,
    [Parameter(Mandatory=$true)][string]$InputDirectory,
    [Parameter(Mandatory=$true)][string]$ArtifactDirectory
)
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.IO.Compression
Add-Type -AssemblyName System.IO.Compression.FileSystem
$windowsRoot = Split-Path $PSScriptRoot -Parent
$projectRoot = Split-Path $windowsRoot -Parent
$versionSource = [IO.File]::ReadAllText((Join-Path $windowsRoot 'CodexMeter.Core/ProductVersion.cs'))
$version = [regex]::Match($versionSource, 'Current = "([^"]+)"').Groups[1].Value
if (!$version) { throw 'Missing product version.' }
$inputRoot = (Resolve-Path -LiteralPath $InputDirectory).Path
$artifactRoot = [IO.Path]::GetFullPath($ArtifactDirectory)
$runtimeName = if ($Runtime -eq 'Framework') { 'framework-x64' } else { 'dotnet10-x64' }
$applicationFiles = if ($Runtime -eq 'Framework') { @('CodexMeter.exe','CodexMeter.Core.dll','CodexMeter.exe.config') } else { @('CodexMeter.exe') }
foreach ($name in $applicationFiles) {
    if (!(Test-Path -LiteralPath (Join-Path $inputRoot $name) -PathType Leaf)) { throw ('Missing application file: ' + $name) }
}
if ($Runtime -eq 'DotNet') {
    # Never obtain a tidy archive by dropping required unbundled dependencies.
    $unexpected = @(Get-ChildItem -LiteralPath $inputRoot -Force | Where-Object { $_.Name -ne 'CodexMeter.exe' -and !(!$_.PSIsContainer -and $_.Extension -eq '.pdb') })
    if ($unexpected.Count) { throw 'Single-file publish emitted extra dependencies or content; refuse to omit them.' }
}
[IO.Directory]::CreateDirectory($artifactRoot) | Out-Null
$portable = Join-Path $artifactRoot ('portable-' + $runtimeName + '-' + [Guid]::NewGuid().ToString('N'))
[IO.Directory]::CreateDirectory($portable) | Out-Null
$docs = Join-Path $portable 'docs'
[IO.Directory]::CreateDirectory($docs) | Out-Null
foreach ($name in $applicationFiles) { Copy-Item -LiteralPath (Join-Path $inputRoot $name) -Destination $portable }
Copy-Item -LiteralPath (Join-Path $projectRoot 'LICENSE') -Destination $docs
Copy-Item -LiteralPath (Join-Path $projectRoot 'UPSTREAM.md') -Destination $docs
foreach ($name in @('README.md','VERIFICATION.md','PARITY.md','RELEASES.md','ACCEPTANCE.md')) { Copy-Item -LiteralPath (Join-Path $windowsRoot $name) -Destination $docs }
# Release notes live in the repository, not in each portable documentation folder.
$releaseGuide = Join-Path $docs 'RELEASES.md'
$releaseText = [IO.File]::ReadAllText($releaseGuide)
$releaseText = [regex]::Replace($releaseText, '\]\(releases/([^\s)]+)\)', ('](https://github.com/bsxucome/CodexMeter-for-Windows/blob/windows-v' + $version + '/windows/releases/$1)'))
[IO.File]::WriteAllText($releaseGuide, $releaseText, (New-Object Text.UTF8Encoding($false)))
$guide = @"
CodexMeter for Windows $version

开始使用：完整解压后，双击本目录中的 CodexMeter.exe。
先安装 Windows 原生 Codex CLI，并运行 codex login 登录。
升级时先退出旧版，将新版解压到独立目录；设置与历史会保留。
详细指南与开源协议位于 docs 文件夹。兼容版的 DLL 和配置文件请勿删除。
.NET 10 版为自包含单文件，首次启动可能向系统临时目录释放运行时依赖。

Get started: extract the entire archive, then run CodexMeter.exe in this folder.
Install the native Windows Codex CLI and sign in with codex login first.
Exit the old version before upgrading. Extract into a new folder; preferences and history remain available.
See docs for the full guide and license. Keep the Framework DLL and configuration file beside the executable.
The self-contained .NET 10 executable may extract native runtime files into the system temporary folder.

项目主页 / Project: https://github.com/bsxucome/CodexMeter-for-Windows
English: https://github.com/bsxucome/CodexMeter-for-Windows/blob/main/README.en.md
"@
[IO.File]::WriteAllText((Join-Path $portable '使用说明.txt'), $guide.Replace("`r`n", "`n").Replace("`n", "`r`n"), (New-Object Text.UTF8Encoding($true)))
$archive = Join-Path $artifactRoot "CodexMeter-Windows-$version-$runtimeName.zip"
$stream = [IO.File]::Open($archive, [IO.FileMode]::Create, [IO.FileAccess]::Write, [IO.FileShare]::None)
try {
    $zip = New-Object IO.Compression.ZipArchive -ArgumentList $stream, ([IO.Compression.ZipArchiveMode]::Create), $false
    try {
        foreach ($file in Get-ChildItem -LiteralPath $portable -Recurse -File) {
            $relative = $file.FullName.Substring($portable.Length + 1).Replace('\','/')
            [IO.Compression.ZipFileExtensions]::CreateEntryFromFile($zip, $file.FullName, $relative, [IO.Compression.CompressionLevel]::Optimal) | Out-Null
        }
    } finally { $zip.Dispose() }
} finally { $stream.Dispose() }
$hash = (Get-FileHash -LiteralPath $archive -Algorithm SHA256).Hash.ToLowerInvariant()
[IO.File]::WriteAllText($archive + '.sha256', $hash + '  ' + [IO.Path]::GetFileName($archive) + "`n", (New-Object Text.UTF8Encoding($false)))
Write-Output ('Package: ' + $archive)
Write-Output ('Portable directory: ' + $portable)
