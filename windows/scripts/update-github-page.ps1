param()
$ErrorActionPreference = 'Stop'
$projectRoot = [IO.Path]::GetFullPath((Split-Path (Split-Path $PSScriptRoot -Parent) -Parent))
$profile = ConvertFrom-Json -InputObject ([IO.File]::ReadAllText((Join-Path $projectRoot '.github/repository-profile.json')))
$repository = 'bsxucome/CodexMeter-for-Windows'
$legacyRepository = 'bsxucome/CodexMeter-Windows'
if ($profile.repository -ne $repository -or $profile.displayName -ne 'CodexMeter for Windows' -or !$profile.description -or $profile.description.Length -gt 350 -or $profile.homepage -ne "https://github.com/$repository#readme") { throw 'Unexpected repository profile. Nothing was published.' }
if (@($profile.topics).Count -gt 20 -or @($profile.topics | Where-Object { $_ -notmatch '^[a-z0-9][a-z0-9-]{0,49}$' }).Count) { throw 'Invalid repository topics.' }
$gitPath = (Get-Command git -CommandType Application -ErrorAction Stop | Select-Object -First 1).Source
$gitArguments = @('-c', ('safe.directory=' + $projectRoot.Replace('\','/')), '-C', $projectRoot)
$allowedRemotes = @("https://github.com/$repository.git", "https://github.com/$legacyRepository.git")
$pushRemotes = @(& $gitPath @gitArguments remote get-url --push --all origin)
if ($LASTEXITCODE -ne 0 -or $pushRemotes.Count -ne 1 -or $pushRemotes[0] -notin $allowedRemotes) { throw 'Push origin does not match the authorized repository.' }
$fetchRemotes = @(& $gitPath @gitArguments remote get-url --all origin)
if ($LASTEXITCODE -ne 0 -or $fetchRemotes.Count -ne 1 -or $fetchRemotes[0] -notin $allowedRemotes) { throw 'Fetch origin does not match the authorized repository.' }
$branch = & $gitPath @gitArguments branch --show-current
if ($LASTEXITCODE -ne 0 -or $branch -ne 'main') { throw 'Expected main.' }
$dirty = & $gitPath @gitArguments status --porcelain --untracked-files=no
if ($LASTEXITCODE -ne 0 -or $dirty) { throw 'Commit the prepared page changes before publishing.' }
$pageCommit = & $gitPath @gitArguments rev-parse HEAD
if ($LASTEXITCODE -ne 0 -or $pageCommit -notmatch '^[a-f0-9]{40}$') { throw 'Could not identify the committed page source.' }
$ghCommand = Get-Command gh -CommandType Application -ErrorAction SilentlyContinue | Select-Object -First 1
$ghPath = if ($ghCommand) { $ghCommand.Source } else { $null }
if (!$ghPath) {
    $cache = Join-Path $projectRoot '.tools'
    if (Test-Path -LiteralPath $cache) { $ghPath = Get-ChildItem -LiteralPath $cache -Directory -Filter 'github-cli-*' | Sort-Object LastWriteTime -Descending | ForEach-Object { Join-Path $_.FullName 'bin/gh.exe' } | Where-Object { Test-Path -LiteralPath $_ } | Select-Object -First 1 }
}
if (!$ghPath) { throw 'GitHub CLI is unavailable. Use setup-github-cli.ps1 in your normal Windows session.' }
$destination = Join-Path $projectRoot 'windows/artifacts/github-page'
[IO.Directory]::CreateDirectory($destination) | Out-Null
$operation = Join-Path $destination ([Guid]::NewGuid().ToString('N'))
[IO.Directory]::CreateDirectory($operation) | Out-Null
$commandIndex = 0
function Invoke-PageGithub([string[]]$Arguments, [switch]$AllowNotFound) {
    $script:commandIndex++
    $outputPath = Join-Path $operation ("command-$script:commandIndex.stdout.log")
    $errorPath = Join-Path $operation ("command-$script:commandIndex.stderr.log")
    $quoted = @($Arguments | ForEach-Object { '"' + [regex]::Replace([regex]::Replace($_, '(\\*)"', '$1$1\"'), '(\\+)$', '$1$1') + '"' })
    $process = Start-Process -FilePath $ghPath -ArgumentList ($quoted -join ' ') -WorkingDirectory $projectRoot -WindowStyle Hidden -Wait -PassThru -RedirectStandardOutput $outputPath -RedirectStandardError $errorPath
    try { $result = $process.ExitCode } finally { $process.Dispose() }
    if ($result -ne 0) {
        $message = [IO.File]::ReadAllText($errorPath)
        if ($AllowNotFound -and $message -match '\bHTTP 404\b') { return $null }
        $message = [regex]::Replace($message, 'github_pat_[A-Za-z0-9_]+|gh[pousr]_[A-Za-z0-9]{20,}', '[redacted]')
        $message = [regex]::Replace($message, '(?i)(https?://)[^/\s@]+@', '$1[redacted]@')
        throw ('GitHub command failed (exit ' + $result + '): ' + $message.Trim())
    }
    return [IO.File]::ReadAllText($outputPath)
}
$summaryPath = Join-Path $destination 'latest-result.json'
try {
    $lookupRepository = if ($pushRemotes[0] -eq "https://github.com/$legacyRepository.git") { $legacyRepository } else { $repository }
    $before = ConvertFrom-Json -InputObject (Invoke-PageGithub @('api', "repos/$lookupRepository"))
    if ($before.full_name -notin @($repository, $legacyRepository) -or !$before.permissions.admin) { throw 'Repository identity or administration permission was not confirmed.' }
    $topics = @(@($before.topics) + @($profile.topics) | Where-Object { $_ } | Sort-Object -Unique)
    if ($topics.Count -gt 20) { throw 'The combined topic list exceeds GitHub limits. Existing topics were preserved.' }
    $bodyPath = Join-Path $operation 'profile.json'
    $body = @{ description = $profile.description; homepage = $profile.homepage }
    if ($before.full_name -eq $legacyRepository) { $body.name = 'CodexMeter-for-Windows' }
    [IO.File]::WriteAllText($bodyPath, ($body | ConvertTo-Json), (New-Object Text.UTF8Encoding($false)))
    $null = Invoke-PageGithub @('api', '--method', 'PATCH', ('repos/' + $before.full_name), '--input', $bodyPath)
    $renamed = ConvertFrom-Json -InputObject (Invoke-PageGithub @('api', "repos/$repository"))
    if ($renamed.full_name -ne $repository -or $renamed.description -ne $profile.description -or $renamed.homepage -ne $profile.homepage) { throw 'Repository rename/profile could not be confirmed. Local origin was not changed.' }
    # Change only the two previously validated URLs after GitHub confirms the
    # rename. On partial failure the exact old/new allowlist permits a safe retry.
    if ($fetchRemotes[0] -ne "https://github.com/$repository.git") {
        & $gitPath @gitArguments remote set-url origin "https://github.com/$repository.git"
        if ($LASTEXITCODE -ne 0) { throw 'Could not update the local fetch origin after repository rename.' }
    }
    if ($pushRemotes[0] -ne "https://github.com/$repository.git") {
        & $gitPath @gitArguments remote set-url --push origin "https://github.com/$repository.git"
        if ($LASTEXITCODE -ne 0) { throw 'Could not update the local push origin after repository rename.' }
    }
    # Publish committed source without a polling loop or a version tag/Release.
    & (Join-Path $PSScriptRoot 'sync-ci-fix.ps1') -SkipWait | Out-Host
    $remoteHead = ConvertFrom-Json -InputObject (Invoke-PageGithub @('api', "repos/$repository/git/ref/heads/main"))
    if ($remoteHead.object.sha -ne $pageCommit) { throw 'GitHub main does not match the inspected page commit. Publication was not confirmed.' }
    $topicsPath = Join-Path $operation 'topics.json'
    [IO.File]::WriteAllText($topicsPath, (@{ names = $topics } | ConvertTo-Json), (New-Object Text.UTF8Encoding($false)))
    $null = Invoke-PageGithub @('api', '--method', 'PUT', "repos/$repository/topics", '--input', $topicsPath)
    # Local version notes provide the reviewed Chinese copy. Update text only;
    # do not create a Release or replace its tag, assets, visibility or status.
    $notesRoot = Join-Path $projectRoot 'windows/releases'
    if (Test-Path -LiteralPath $notesRoot) {
        foreach ($note in Get-ChildItem -LiteralPath $notesRoot -Filter '*.md' -File) {
            if ($note.BaseName -notmatch '^[0-9]+\.[0-9]+\.[0-9]+(-[0-9A-Za-z.-]+)?$') { continue }
            $tag = 'windows-v' + $note.BaseName
            $releaseJson = Invoke-PageGithub -Arguments @('api', "repos/$repository/releases/tags/$tag") -AllowNotFound
            if (!$releaseJson) { continue }
            $release = ConvertFrom-Json -InputObject $releaseJson
            if ($release.id.ToString() -notmatch '^[0-9]+$' -or $release.tag_name -ne $tag -or $release.draft) { throw 'Existing public release identity was not confirmed. Release text was not changed.' }
            $releaseBody = [IO.File]::ReadAllText($note.FullName, [Text.Encoding]::UTF8) + "`n`n---`n`n[中文介绍](https://github.com/$repository#readme) · [English](https://github.com/$repository/blob/main/README.en.md)`n"
            $releaseTitle = 'CodexMeter for Windows ' + $note.BaseName
            $releasePath = Join-Path $operation ($tag + '.json')
            [IO.File]::WriteAllText($releasePath, (@{ name = $releaseTitle; body = $releaseBody } | ConvertTo-Json), (New-Object Text.UTF8Encoding($false)))
            $null = Invoke-PageGithub @('api', '--method', 'PATCH', "repos/$repository/releases/$($release.id)", '--input', $releasePath)
            $verifiedRelease = ConvertFrom-Json -InputObject (Invoke-PageGithub @('api', "repos/$repository/releases/tags/$tag"))
            if ($verifiedRelease.name -ne $releaseTitle -or $verifiedRelease.body -ne $releaseBody) { throw 'The release text update was not confirmed.' }
        }
    }
    $after = ConvertFrom-Json -InputObject (Invoke-PageGithub @('api', "repos/$repository"))
    if ($after.full_name -ne $repository -or $after.description -ne $profile.description -or $after.homepage -ne $profile.homepage -or @($topics | Where-Object { $_ -notin @($after.topics) }).Count) { throw 'GitHub page changes were not fully confirmed. Some steps may already have completed; rerunning is safe.' }
    [IO.File]::WriteAllText($summaryPath, (@{ status = 'published'; repository = $repository; commit = $pageCommit; description = $after.description; homepage = $after.homepage; topics = $after.topics; url = "https://github.com/$repository" } | ConvertTo-Json -Depth 4), (New-Object Text.UTF8Encoding($false)))
    Write-Output ('Page published and verified: https://github.com/' + $repository)
    Write-Output 'README is Chinese by default, with an English link. Source changes were pushed; build progress remains in GitHub Actions.'
} catch {
    [IO.File]::WriteAllText($summaryPath, (@{ status = 'not_confirmed'; repository = $repository } | ConvertTo-Json))
    throw
}
