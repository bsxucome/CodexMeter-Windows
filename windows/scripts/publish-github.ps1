param(
    [string]$Owner,
    [ValidatePattern('^[A-Za-z0-9_.-]+$')][string]$RepositoryName = 'CodexMeter-Windows',
    [ValidateSet('public','private')][string]$Visibility = 'public',
    [switch]$PrepareOnly
)
$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path (Split-Path $PSScriptRoot -Parent) -Parent
Push-Location -LiteralPath $projectRoot
try {
    if (!(Test-Path -LiteralPath '.git')) { git init --initial-branch=main; if ($LASTEXITCODE -ne 0) { throw 'Git initialization failed.' } }
    $selection = @('.gitattributes','.gitignore','.github','AGENTS.md','CONTRIBUTING.md','LICENSE','README.md','UPSTREAM.md','windows')
    if (Test-Path -LiteralPath 'docs') { $selection += 'docs' }
    git add -- @selection
    if ($LASTEXITCODE -ne 0) { throw 'Source staging failed.' }
    $files = @(git diff --cached --name-only)
    if (@($files | Where-Object { $_ -match '(^|/)(artifacts|\.tools|bin|obj)(/|$)|(^|/)(auth\.json|settings\.json|remote-secrets\.bin|history\.sqlite3)|\.pfx$|\.p12$|private[^/]*\.xml$' }).Count) { throw 'Local data or private files entered the source selection.' }
    $credentialFiles = @(git grep --cached -l -E 'github_pat_[A-Za-z0-9_]{20,}|gh[pousr]_[A-Za-z0-9]{20,}|sk-proj-[A-Za-z0-9_-]{20,}|BEGIN (RSA |EC |OPENSSH )?PRIVATE KEY' -- .)
    if ($LASTEXITCODE -gt 1) { throw 'Source credential scan failed.' }
    if ($credentialFiles.Count) { throw ('Credential-like content found in: ' + ($credentialFiles -join ', ')) }
    git diff --cached --check
    if ($LASTEXITCODE -ne 0) { throw 'Staged whitespace validation failed.' }
    $artifactRoot = Join-Path $projectRoot 'windows/artifacts'
    [IO.Directory]::CreateDirectory($artifactRoot) | Out-Null
    $tree = (git write-tree).Trim()
    if ($LASTEXITCODE -ne 0) { throw 'Could not create the source tree.' }
    $sourceArchive = Join-Path $artifactRoot 'CodexMeter-Windows-source.zip'
    git archive --format=zip --output=$sourceArchive $tree
    if ($LASTEXITCODE -ne 0) { throw 'Source archive failed.' }
    Write-Output "Prepared source: $sourceArchive"
    Write-Output ('Tracked source files: ' + @(git ls-files).Count)
    if ($PrepareOnly) { return }
    $gh = Get-Command gh -ErrorAction SilentlyContinue
    $ghPath = if ($gh) { $gh.Source } else { & (Join-Path $PSScriptRoot 'setup-github-cli.ps1') }
    if (!$ghPath -or !(Test-Path -LiteralPath $ghPath)) { throw 'GitHub CLI is unavailable. Prepared source remains available.' }
    & $ghPath auth status --hostname github.com *> $null
    if ($LASTEXITCODE -ne 0) {
        & $ghPath auth login --web --hostname github.com --git-protocol https
        if ($LASTEXITCODE -ne 0) { throw 'GitHub browser authorization did not complete.' }
    }
    $identity = (& $ghPath api user | ConvertFrom-Json)
    if ($LASTEXITCODE -ne 0 -or !$identity.login) { throw 'Cannot identify the authenticated GitHub account.' }
    if (!$Owner) { $Owner = $identity.login }
    if ($Owner -notmatch '^[A-Za-z0-9][A-Za-z0-9-]*$') { throw 'Invalid repository owner.' }
    $repository = "$Owner/$RepositoryName"
    $remote = "https://github.com/$repository.git"
    $origin = if (@(git remote) -contains 'origin') { git remote get-url origin } else { $null }
    if ($origin -and $origin -ne $remote) { throw 'Origin already points elsewhere. No remote was changed.' }
    if (!(git config --get user.name)) { git config --local user.name $identity.login }
    if (!(git config --get user.email)) { git config --local user.email ($identity.id.ToString() + '+' + $identity.login + '@users.noreply.github.com') }
    git diff --cached --quiet
    if ($LASTEXITCODE -eq 1) { git commit -m 'Complete Windows quota companion, recovery tests and GitHub release automation'; if ($LASTEXITCODE -ne 0) { throw 'Commit failed.' } }
    & $ghPath repo view $repository --json nameWithOwner *> $null
    $exists = $LASTEXITCODE -eq 0
    if ($exists -and !$origin) { throw 'That repository already exists; choose a new repository name. Nothing was pushed.' }
    if (!$exists) {
        & $ghPath repo create $repository "--$Visibility" --source . --remote origin --description 'Windows taskbar companion for Codex quota, history and optional notifications. Independent adaptation of raycalrui/CodexMeter.'
        if ($LASTEXITCODE -ne 0) { throw 'Repository creation failed.' }
    }
    & $ghPath auth setup-git --hostname github.com
    if ($LASTEXITCODE -ne 0) { throw 'GitHub Git authentication setup failed.' }
    git push --set-upstream origin main
    if ($LASTEXITCODE -ne 0) { throw 'Push failed. The prepared local commit and remote are retained for retry.' }
    & $ghPath repo view $repository --json url --jq .url
    if ($LASTEXITCODE -ne 0) { throw 'Repository URL verification failed.' }
} finally { Pop-Location }
