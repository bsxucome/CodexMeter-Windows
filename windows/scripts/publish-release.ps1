param(
    [Parameter(Mandatory=$true)][ValidatePattern('^[A-Za-z0-9_.-]+/[A-Za-z0-9_.-]+$')][string]$Repository,
    [Parameter(Mandatory=$true)][ValidatePattern('^windows-v[0-9]+\.[0-9]+\.[0-9]+(-[0-9A-Za-z.-]+)?$')][string]$Tag,
    [Parameter(Mandatory=$true)][string]$Directory,
    [switch]$CreateTag,
    [ValidatePattern('^[a-f0-9]{40}$')][string]$Commit
)
$ErrorActionPreference = 'Stop'
$version = $Tag.Substring(9)
$assets = @()
foreach ($runtime in @('framework-x64','dotnet10-x64')) {
    $name = "CodexMeter-Windows-$version-$runtime.zip"
    $archive = Join-Path $Directory $name
    if (!(Test-Path -LiteralPath $archive) -or !(Test-Path -LiteralPath ($archive + '.sha256'))) { throw "Missing checked package: $name" }
    $expected = [IO.File]::ReadAllText($archive + '.sha256').Split(' ')[0]
    if ($expected -notmatch '^[a-f0-9]{64}$' -or (Get-FileHash -LiteralPath $archive -Algorithm SHA256).Hash.ToLowerInvariant() -ne $expected) { throw "Package checksum mismatch: $name" }
    $assets += $archive
    $assets += $archive + '.sha256'
}
$feeds = @(Get-ChildItem -LiteralPath $Directory -File -Filter '*.json' | Where-Object { $_.Name -match '^(stable|beta)-(framework|dotnet10)-x64\.json$|^public-key-(framework|dotnet10)-x64\.json$' })
$assets += $feeds.FullName
$notes = Join-Path $Directory 'release-notes.md'
$text = "## 下载与使用`n`n提供 .NET 10 自包含包与 .NET Framework 4.8+ 兼容包，均为 Windows x64 便携版。请先退出旧版，完整解压后运行 CodexMeter.exe。使用说明、功能范围与验证记录见包内文档。`n`n[中文介绍](https://github.com/$Repository#readme) · [English](https://github.com/$Repository/blob/main/README.en.md)`n`n"
$text += if ($feeds.Count) { "已附签名更新清单与公钥；请仅配置信任渠道获取的公钥。`n" } else { "当前未配置更新签名密钥，本次不发布签名更新源。代码签名状态见包内验证记录。`n" }
$preparedNotes = Join-Path (Split-Path $PSScriptRoot -Parent) ("releases/$version.md")
if (Test-Path -LiteralPath $preparedNotes) { $text = [IO.File]::ReadAllText($preparedNotes, [Text.Encoding]::UTF8) + "`n`n---`n`n" + $text }
[IO.File]::WriteAllText($notes, $text, (New-Object Text.UTF8Encoding($false)))
if ($CreateTag) {
    if (!$Commit) { throw 'The immutable build commit is required to create a release tag.' }
    $actualCommit = & git rev-parse HEAD
    if ($LASTEXITCODE -ne 0 -or $actualCommit -ne $Commit) { throw 'The checked-out source does not match the built commit.' }
    . (Join-Path $PSScriptRoot 'actions-release-common.ps1')
    $projectRoot = Split-Path (Split-Path $PSScriptRoot -Parent) -Parent
    if ((Read-ActionsProductVersion $projectRoot) -ne $version) { throw 'Release tag does not match the product version.' }
    Assert-ActionsReleaseAvailable -Repository $Repository -Tag $Tag -Commit $Commit
    New-ActionsReleaseTag -Repository $Repository -Tag $Tag -Commit $Commit
}
$arguments = @('release','create',$Tag,'--repo',$Repository,'--verify-tag','--title',"CodexMeter for Windows $version",'--notes-file',$notes)
if ($version.Contains('-')) { $arguments += '--prerelease' }
& gh @arguments @assets
if ($LASTEXITCODE -ne 0) { throw 'GitHub Release publication failed. Existing releases/assets were not overwritten.' }
if ($feeds.Count) {
    & gh release view windows-update-feed --repo $Repository *> $null
    if ($LASTEXITCODE -ne 0) {
        & gh release create windows-update-feed --repo $Repository --target main --latest=false --title 'Windows 签名更新源' --notes '提供各 Windows 运行时独立签名的稳定版与测试版更新清单。'
        if ($LASTEXITCODE -ne 0) { throw 'Could not create the Windows feed release.' }
    }
    & gh release upload windows-update-feed --repo $Repository --clobber @($feeds.FullName)
    if ($LASTEXITCODE -ne 0) { throw 'Signed feed publication failed; versioned release assets remain available.' }
}
if ($CreateTag) {
    $url = Confirm-ActionsPublishedRelease -Repository $Repository -Tag $Tag -Commit $Commit
    if ($env:GITHUB_STEP_SUMMARY) {
        [IO.File]::AppendAllText($env:GITHUB_STEP_SUMMARY, "已发布 [CodexMeter for Windows $version]($url)，包含两个运行时的便携包与 SHA-256 校验文件。`n", (New-Object Text.UTF8Encoding($false)))
    }
    Write-Output ('Published and verified: ' + $url)
}
