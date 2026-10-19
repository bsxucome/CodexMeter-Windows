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
$text = "CodexMeter for Windows $version`n`n.NET 10 self-contained and .NET Framework 4.8+ portable x64 packages. Extract all files before launching. Exit the old version first. See README.md, PARITY.md and VERIFICATION.md for implemented features and desktop acceptance limits.`n`n"
$text += if ($feeds.Count) { "Signed Windows update manifests and public keys are attached. Configure only a public key obtained through a trusted channel.`n" } else { "No update signing secret was configured; no signed feed is published. Code-signing status is documented in the packaged verification notes.`n" }
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
$arguments = @('release','create',$Tag,'--repo',$Repository,'--verify-tag','--title',"CodexMeter Windows $version",'--notes-file',$notes)
if ($version.Contains('-')) { $arguments += '--prerelease' }
& gh @arguments @assets
if ($LASTEXITCODE -ne 0) { throw 'GitHub Release publication failed. Existing releases/assets were not overwritten.' }
if ($feeds.Count) {
    & gh release view windows-update-feed --repo $Repository *> $null
    if ($LASTEXITCODE -ne 0) {
        & gh release create windows-update-feed --repo $Repository --target main --latest=false --title 'Windows signed update feeds' --notes 'Stable and beta manifests, separately signed for each Windows runtime. This release does not contain macOS packages.'
        if ($LASTEXITCODE -ne 0) { throw 'Could not create the Windows feed release.' }
    }
    & gh release upload windows-update-feed --repo $Repository --clobber @($feeds.FullName)
    if ($LASTEXITCODE -ne 0) { throw 'Signed feed publication failed; versioned release assets remain available.' }
}
if ($CreateTag) {
    $url = Confirm-ActionsPublishedRelease -Repository $Repository -Tag $Tag -Commit $Commit
    if ($env:GITHUB_STEP_SUMMARY) {
        [IO.File]::AppendAllText($env:GITHUB_STEP_SUMMARY, "Published [CodexMeter Windows $version]($url) with both portable packages and SHA-256 files.`n", (New-Object Text.UTF8Encoding($false)))
    }
    Write-Output ('Published and verified: ' + $url)
}
