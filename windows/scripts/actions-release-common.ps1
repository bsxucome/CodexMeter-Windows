# Shared by the read-only plan job and the write-scoped publication job.
function Invoke-ActionsGithub {
    param([ValidatePattern('^[A-Za-z0-9_.-]+/[A-Za-z0-9_.-]+$')][string]$Repository,
        [ValidateSet('GET','POST')][string]$Method = 'GET', [string]$Path, $Body, [switch]$AllowNotFound)
    if (!$env:GH_TOKEN) { throw 'The Actions GitHub token is unavailable.' }
    if ($Path -notmatch '^(releases/tags/windows-v[0-9A-Za-z.-]+|git/ref/tags/windows-v[0-9A-Za-z.-]+|git/tags/[a-f0-9]{40}|git/refs)$') { throw 'Unexpected GitHub API path.' }
    $parameters = @{
        Uri = "https://api.github.com/repos/$Repository/$Path"; Method = $Method
        Headers = @{ Authorization = 'Bearer ' + $env:GH_TOKEN; Accept = 'application/vnd.github+json'; 'X-GitHub-Api-Version' = '2022-11-28' }
        UserAgent = 'CodexMeter-Windows-Actions'; MaximumRedirection = 0; TimeoutSec = 60; ErrorAction = 'Stop'
    }
    if ($null -ne $Body) { $parameters.Body = $Body | ConvertTo-Json -Compress; $parameters.ContentType = 'application/json' }
    try { return Invoke-RestMethod @parameters }
    catch {
        $response = $_.Exception.Response
        $status = if ($response) { [int]$response.StatusCode } else { 0 }
        if ($AllowNotFound -and $Method -eq 'GET' -and $status -eq 404) { return $null }
        # Do not expose headers, tokens or raw response bodies in error records.
        throw [InvalidOperationException]::new("GitHub $Method request failed (HTTP $status).")
    }
}
function Get-ActionsPowerShellSteps([string]$WorkflowPath) {
    $yaml = [IO.File]::ReadAllText($WorkflowPath)
    foreach ($step in [regex]::Split($yaml, '(?m)(?=^      - )')) {
        if ($step -notmatch '(?m)^        shell: powershell\s*$') { continue }
        $run = [regex]::Match($step, '(?m)^        run: ([^\r\n]*)(?<body>(?:\r?\n(?:          [^\r\n]*|[ \t]*))*)')
        if (!$run.Success) { throw 'PowerShell workflow step has no supported run block.' }
        $text = $run.Groups[1].Value
        if ($text -match '^\|[-+]?$') { $text = [regex]::Replace($run.Groups['body'].Value, '(?m)^          ', '').Trim("`r", "`n") }
        [pscustomobject]@{ Text = $text }
    }
}
function Read-ActionsProductVersion([string]$ProjectRoot) {
    $source = [IO.File]::ReadAllText((Join-Path $ProjectRoot 'windows/CodexMeter.Core/ProductVersion.cs'))
    $version = [regex]::Match($source, 'Current = "([0-9]+\.[0-9]+\.[0-9]+(?:-[0-9A-Za-z.-]+)?)"').Groups[1].Value
    $assembly = [regex]::Match($source, 'Assembly = "([0-9]+\.[0-9]+\.[0-9]+\.[0-9]+)"').Groups[1].Value
    [xml]$project = [IO.File]::ReadAllText((Join-Path $ProjectRoot 'windows/CodexMeter.Windows/CodexMeter.Windows.csproj'))
    [xml]$manifest = [IO.File]::ReadAllText((Join-Path $ProjectRoot 'windows/CodexMeter.Windows/app.manifest'))
    $identity = $manifest.SelectSingleNode('//*[local-name()="assemblyIdentity"]')
    $baseVersion = ($version -split '-', 2)[0]
    if (!$version -or !$assembly -or $assembly -notmatch ('^' + [regex]::Escape($baseVersion) + '\.[0-9]+$') -or $project.Project.PropertyGroup.Version -ne $version -or !$identity -or $identity.GetAttribute('version') -ne $assembly) { throw 'Product, SDK and manifest versions must agree before building.' }
    return $version
}
function Get-ActionsTagCommit {
    param([string]$Repository, [ValidatePattern('^windows-v[0-9]+\.[0-9]+\.[0-9]+(-[0-9A-Za-z.-]+)?$')][string]$Tag)
    $reference = Invoke-ActionsGithub -Repository $Repository -Path "git/ref/tags/$Tag" -AllowNotFound
    if (!$reference) { return $null }
    $target = $reference.object
    for ($depth = 0; $depth -lt 8; $depth++) {
        if (!$target -or $target.sha -notmatch '^[a-f0-9]{40}$') { throw 'Invalid GitHub tag target.' }
        if ($target.type -eq 'commit') { return $target.sha }
        if ($target.type -ne 'tag') { throw 'Release tag does not resolve to a commit.' }
        $annotated = Invoke-ActionsGithub -Repository $Repository -Path ('git/tags/' + $target.sha)
        $target = $annotated.object
    }
    throw 'Release tag annotation chain exceeds the supported depth.'
}
function Assert-ActionsReleaseAvailable {
    param([string]$Repository, [string]$Tag, [ValidatePattern('^[a-f0-9]{40}$')][string]$Commit)
    $target = Get-ActionsTagCommit -Repository $Repository -Tag $Tag
    if ($target -and $target -ne $Commit) { throw 'The release tag already points to another commit. Use a new version.' }
    $release = Invoke-ActionsGithub -Repository $Repository -Path "releases/tags/$Tag" -AllowNotFound
    if ($release) { throw 'This version already has a GitHub Release. Update the product version before publishing again.' }
}
function Get-ActionsReleasePlan {
    param([string]$ProjectRoot, [string]$Repository, [ValidatePattern('^[a-f0-9]{40}$')][string]$Commit,
        [ValidateSet('push','pull_request','workflow_dispatch')][string]$EventName, [string]$Reference,
        [ValidateSet('build','release')][string]$Operation = 'build')
    $version = Read-ActionsProductVersion $ProjectRoot
    $tag = 'windows-v' + $version
    $publish = $false
    if ($EventName -eq 'workflow_dispatch' -and $Operation -eq 'release') {
        if ($Reference -ne 'refs/heads/main') { throw 'Manual publication must run from main.' }
        $publish = $true
    } elseif ($EventName -eq 'push' -and $Reference.StartsWith('refs/tags/windows-v')) {
        if ($Reference -ne ('refs/tags/' + $tag)) { throw 'Release tag does not match the product version.' }
        $publish = $true
    }
    if ($publish) { Assert-ActionsReleaseAvailable -Repository $Repository -Tag $tag -Commit $Commit }
    return [pscustomobject]@{ Version = $version; Tag = $tag; Publish = $publish; Commit = $Commit }
}
function New-ActionsReleaseTag {
    param([string]$Repository, [ValidatePattern('^windows-v[0-9]+\.[0-9]+\.[0-9]+(-[0-9A-Za-z.-]+)?$')][string]$Tag,
        [ValidatePattern('^[a-f0-9]{40}$')][string]$Commit)
    $target = Get-ActionsTagCommit -Repository $Repository -Tag $Tag
    if (!$target) {
        try { $null = Invoke-ActionsGithub -Repository $Repository -Method POST -Path 'git/refs' -Body @{ ref = 'refs/tags/' + $Tag; sha = $Commit } }
        catch {
            # A concurrent create or a lost response may already have created it.
            $failure = $_
            $target = Get-ActionsTagCommit -Repository $Repository -Tag $Tag
            if (!$target) { throw $failure }
        }
        $target = Get-ActionsTagCommit -Repository $Repository -Tag $Tag
    }
    if ($target -ne $Commit) { throw 'The release tag points to another commit; it was not overwritten.' }
}
function Confirm-ActionsPublishedRelease {
    param([string]$Repository, [string]$Tag, [string]$Commit)
    if ((Get-ActionsTagCommit -Repository $Repository -Tag $Tag) -ne $Commit) { throw 'Published tag no longer matches the built commit.' }
    $release = Invoke-ActionsGithub -Repository $Repository -Path "releases/tags/$Tag"
    $url = "https://github.com/$Repository/releases/tag/$Tag"
    if (!$release -or $release.draft -ne $false -or $release.tag_name -ne $Tag -or $release.html_url -ne $url) { throw 'The requested public GitHub Release was not confirmed.' }
    $version = $Tag.Substring(9)
    foreach ($runtime in @('framework-x64','dotnet10-x64')) {
        foreach ($suffix in @('.zip','.zip.sha256')) {
            $name = "CodexMeter-Windows-$version-$runtime$suffix"
            $assets = @($release.assets | Where-Object { $_.name -ceq $name -and $_.state -eq 'uploaded' -and $_.size -gt 0 })
            if ($assets.Count -ne 1) { throw ('Published asset is missing or incomplete: ' + $name) }
        }
    }
    return $url
}
