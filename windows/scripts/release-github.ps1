param()
$ErrorActionPreference = 'Stop'
$projectRoot = [IO.Path]::GetFullPath((Split-Path (Split-Path $PSScriptRoot -Parent) -Parent))
$repository = 'bsxucome/CodexMeter-Windows'
$destination = Join-Path $projectRoot 'windows/artifacts/release-logs'
[IO.Directory]::CreateDirectory($destination) | Out-Null
$gitPath = (Get-Command git -CommandType Application -ErrorAction Stop | Select-Object -First 1).Source
$gitArguments = @('-c', ('safe.directory=' + $projectRoot.Replace('\','/')), '-C', $projectRoot)
$ghCommand = Get-Command gh -CommandType Application -ErrorAction SilentlyContinue | Select-Object -First 1
$ghPath = if ($ghCommand) { $ghCommand.Source } else { $null }
if (!$ghPath) {
    $cache = Join-Path $projectRoot '.tools'
    if (Test-Path -LiteralPath $cache) {
        $ghPath = @(Get-ChildItem -LiteralPath $cache -Directory -Filter 'github-cli-*' | Sort-Object LastWriteTime -Descending | ForEach-Object { Join-Path $_.FullName 'bin/gh.exe' } | Where-Object { Test-Path -LiteralPath $_ } | Select-Object -First 1)[0]
    }
}
$script:commandIndex = 0
function Invoke-ReleaseNative([string]$Executable, [string[]]$Arguments, [switch]$AllowFailure) {
    $script:commandIndex++
    $output = Join-Path $destination ("command-$script:commandIndex.stdout.log")
    $errorFile = Join-Path $destination ("command-$script:commandIndex.stderr.log")
    # Quote argv using the Windows CRT rules, including paths with spaces.
    $quoted = @($Arguments | ForEach-Object { '"' + [regex]::Replace([regex]::Replace($_, '(\\*)"', '$1$1\"'), '(\\+)$', '$1$1') + '"' })
    $process = Start-Process -FilePath $Executable -ArgumentList ($quoted -join ' ') -WorkingDirectory $projectRoot -WindowStyle Hidden -Wait -PassThru -RedirectStandardOutput $output -RedirectStandardError $errorFile
    try { $script:nativeExit = $process.ExitCode } finally { $process.Dispose() }
    $script:nativeError = [regex]::Replace([IO.File]::ReadAllText($errorFile), 'github_pat_[A-Za-z0-9_]+|gh[pousr]_[A-Za-z0-9]{20,}', '[redacted]')
    $script:nativeError = [regex]::Replace($script:nativeError, '(?i)(https?://)[^/\s@]+@', '$1[redacted]@')
    if ($script:nativeExit -ne 0 -and !$AllowFailure) { throw ("Command failed (exit $script:nativeExit). " + $script:nativeError.Trim()) }
    return [IO.File]::ReadAllText($output).Trim()
}
function Invoke-ReleaseGit {
    Invoke-ReleaseNative -Executable $gitPath -Arguments ($gitArguments + @($args))
}
function Read-Release {
    $json = Invoke-ReleaseNative -Executable $ghPath -Arguments @('api', "repos/$repository/releases/tags/$tag") -AllowFailure
    if ($script:nativeExit -ne 0) {
        if ($script:nativeError -match 'HTTP 404') { return $null }
        throw ('Cannot check GitHub Release. ' + $script:nativeError.Trim())
    }
    return ConvertFrom-Json -InputObject $json
}
function Confirm-Release($release) {
    if (!$release -or $release.draft -or $release.tag_name -ne $tag -or $release.html_url -ne "https://github.com/$repository/releases/tag/$tag") { throw 'Published Release metadata does not match the requested version.' }
    foreach ($runtime in @('framework-x64','dotnet10-x64')) {
        foreach ($suffix in @('.zip','.zip.sha256')) {
            $name = "CodexMeter-Windows-$version-$runtime$suffix"
            $asset = @($release.assets | Where-Object { $_.name -ceq $name -and $_.state -eq 'uploaded' -and $_.size -gt 0 })
            if ($asset.Count -ne 1) { throw ('Release asset is missing or incomplete: ' + $name) }
        }
    }
}
function Wait-ReleaseCI([string]$Reference) {
    & (Join-Path $PSScriptRoot 'collect-ci-logs.ps1') -Repository $repository -ExpectedCommit $commit -Reference $Reference -WaitForCompletion | Out-Host
    $result = ConvertFrom-Json -InputObject ([IO.File]::ReadAllText((Join-Path $projectRoot 'windows/artifacts/ci-logs/latest-result.json')))
    $script:runUrl = $result.url
    if ($result.repository -ne $repository -or $result.commit -ne $commit -or $result.reference -ne $Reference -or $result.status -ne 'completed' -or $result.conclusion -ne 'success') {
        throw ('CI has not succeeded for ' + $Reference + '. See windows/artifacts/ci-logs/latest-result.txt; rerun this script after resolving it.')
    }
}
$version = ''; $tag = ''; $commit = ''; $script:runUrl = ''
try {
    if ((Invoke-ReleaseGit remote get-url --push origin) -ne "https://github.com/$repository.git") { throw 'Origin does not match the authorized repository. Nothing was pushed.' }
    if ((Invoke-ReleaseGit branch --show-current) -ne 'main') { throw 'Expected main. Nothing was pushed.' }
    if (Invoke-ReleaseGit status --porcelain --untracked-files=no) { throw 'Tracked changes must be committed before publication. Nothing was pushed.' }
    $commit = Invoke-ReleaseGit rev-parse HEAD
    if ($commit -notmatch '^[a-f0-9]{40}$') { throw 'Invalid HEAD commit.' }
    $source = [IO.File]::ReadAllText((Join-Path $projectRoot 'windows/CodexMeter.Core/ProductVersion.cs'))
    $version = [regex]::Match($source, 'Current = "([0-9]+\.[0-9]+\.[0-9]+(?:-[0-9A-Za-z.-]+)?)"').Groups[1].Value
    if (!$version) { throw 'Missing release version.' }
    $tag = 'windows-v' + $version
    if (!$ghPath -or !(Test-Path -LiteralPath $ghPath)) { throw 'GitHub CLI is unavailable. Run setup-github-cli.ps1 in the normal user session.' }
    $null = Invoke-ReleaseNative -Executable $ghPath -Arguments @('auth','status','--hostname','github.com')
    $localTags = Invoke-ReleaseGit tag --list $tag
    if ($localTags -and (Invoke-ReleaseGit rev-parse ($tag + '^{commit}')) -ne $commit) { throw 'Local release tag points to another commit; it was not changed.' }
    $remoteTags = Invoke-ReleaseGit ls-remote --tags origin ("refs/tags/$tag") ("refs/tags/$tag^{}")
    if ($remoteTags) {
        $lines = @($remoteTags -split "`r?`n")
        $peeled = @($lines | Where-Object { $_.EndsWith('^{}') })
        $target = if ($peeled.Count) { ($peeled[0] -split '\s+')[0] } else { ($lines[0] -split '\s+')[0] }
        if ($target -ne $commit) { throw 'Remote release tag points to another commit; nothing was overwritten.' }
    }
    $release = Read-Release
    if ($release) {
        if (!$remoteTags) { throw 'Existing Release has no matching remote tag.' }
        Confirm-Release $release
    } else {
        Write-Output "Publishing $tag from committed main $commit"
        $null = Invoke-ReleaseGit push origin main
        Wait-ReleaseCI 'main'
        if (!$localTags) { $null = Invoke-ReleaseGit tag $tag $commit }
        if (!$remoteTags) { $null = Invoke-ReleaseGit push origin ("refs/tags/$tag") }
        Wait-ReleaseCI $tag
        $release = Read-Release
        Confirm-Release $release
    }
    $result = @{ status = 'published'; repository = $repository; version = $version; tag = $tag; commit = $commit; url = $release.html_url; runUrl = $script:runUrl; assets = @($release.assets | ForEach-Object name) }
    [IO.File]::WriteAllText((Join-Path $destination 'latest-result.json'), ($result | ConvertTo-Json -Depth 5))
    [IO.File]::WriteAllText((Join-Path $destination 'latest-result.txt'), ("Published: $($release.html_url)`r`nCommit: $commit`r`nAssets: " + ($result.assets -join ', ') + "`r`n"))
    Write-Output ('Published and verified: ' + $release.html_url)
} catch {
    $result = @{ status = 'failed'; repository = $repository; version = $version; tag = $tag; commit = $commit; runUrl = $script:runUrl; error = $_.Exception.Message }
    [IO.File]::WriteAllText((Join-Path $destination 'latest-result.json'), ($result | ConvertTo-Json))
    [IO.File]::WriteAllText((Join-Path $destination 'latest-result.txt'), ('Publication not confirmed: ' + $_.Exception.Message))
    throw
}
