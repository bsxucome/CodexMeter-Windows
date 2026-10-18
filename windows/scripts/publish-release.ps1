param(
    [Parameter(Mandatory=$true)][ValidatePattern('^[A-Za-z0-9_.-]+/[A-Za-z0-9_.-]+$')][string]$Repository,
    [Parameter(Mandatory=$true)][ValidatePattern('^windows-v[0-9]+\.[0-9]+\.[0-9]+(-[0-9A-Za-z.-]+)?$')][string]$Tag,
    [Parameter(Mandatory=$true)][string]$Directory
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
[IO.File]::WriteAllText($notes, $text, (New-Object Text.UTF8Encoding($false)))
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
