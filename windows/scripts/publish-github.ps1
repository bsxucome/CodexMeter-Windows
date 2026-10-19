param(
    [string]$Owner,
    [ValidatePattern('^[A-Za-z0-9_.-]+$')][string]$RepositoryName = 'CodexMeter-Windows',
    [ValidateSet('public','private')][string]$Visibility = 'public',
    [switch]$PrepareOnly
)
$ErrorActionPreference = 'Stop'
$gitExecutable = (Get-Command git -CommandType Application -ErrorAction Stop | Select-Object -First 1).Source
# In Windows PowerShell 5, redirected native stderr becomes NativeCommandError.
# Use native process streams so prompts/progress and expected nonzero probes do
# not terminate the script before their exit codes can be checked.
function Invoke-PublishNative([string]$FilePath, [string[]]$Arguments, [switch]$Capture, [switch]$Quiet) {
    $quoted = foreach ($argument in $Arguments) {
        $escaped = [regex]::Replace($argument, '(\\*)"', '$1$1\"')
        '"' + [regex]::Replace($escaped, '(\\+)$', '$1$1') + '"'
    }
    $start = New-Object Diagnostics.ProcessStartInfo
    $start.FileName = $FilePath
    $start.Arguments = $quoted -join ' '
    $start.WorkingDirectory = (Get-Location).ProviderPath
    $start.UseShellExecute = $false
    $start.CreateNoWindow = $Capture -or $Quiet
    $start.RedirectStandardOutput = $Capture -or $Quiet
    $start.RedirectStandardError = $Capture -or $Quiet
    $process = New-Object Diagnostics.Process
    $process.StartInfo = $start
    try {
        if (!$process.Start()) { throw 'Could not start publication command.' }
        if ($start.RedirectStandardOutput) { $output = $process.StandardOutput.ReadToEndAsync() }
        if ($start.RedirectStandardError) { $errors = $process.StandardError.ReadToEndAsync() }
        $process.WaitForExit()
        if ($start.RedirectStandardOutput) { $text = $output.GetAwaiter().GetResult() }
        $errorText = if ($start.RedirectStandardError) { $errors.GetAwaiter().GetResult() } else { '' }
        # Preserve useful Git errors without letting PS5 turn stderr into a
        # terminating PowerShell error or expose common credential forms.
        $errorText = [regex]::Replace($errorText, '(?i)(https?://)[^/\s@]+@', '$1[redacted]@')
        $errorText = [regex]::Replace($errorText, 'github_pat_[A-Za-z0-9_]+|gh[pousr]_[A-Za-z0-9]{20,}', '[redacted]')
        $script:publishNativeError = if ($Quiet) { '' } else { $errorText.TrimEnd() }
        $script:LASTEXITCODE = $process.ExitCode
        if (!$Quiet -and $errorText.Length) { Write-Host $errorText.TrimEnd() }
        if ($Capture -and !$Quiet -and $text.Length) { Write-Output $text }
    } finally { $process.Dispose() }
}
function Invoke-PublishGit {
    $text = Invoke-PublishNative -FilePath $gitExecutable -Arguments $args -Capture
    if ($text) { $text.TrimEnd([char[]]"`r`n") -split "`r?`n" }
}
$projectRoot = Split-Path (Split-Path $PSScriptRoot -Parent) -Parent
$projectRoot = [IO.Path]::GetFullPath($projectRoot)
# GitHub CLI also starts Git. Inherit an exact-directory exception in this
# process tree instead of changing the user's global Git configuration.
$originalGitCount = [Environment]::GetEnvironmentVariable('GIT_CONFIG_COUNT', 'Process')
$gitCount = 0
if ($originalGitCount -and (![int]::TryParse($originalGitCount, [ref]$gitCount) -or $gitCount -lt 0 -or $gitCount -eq [int]::MaxValue)) { throw 'Invalid GIT_CONFIG_COUNT.' }
$gitKeyName = "GIT_CONFIG_KEY_$gitCount"
$gitValueName = "GIT_CONFIG_VALUE_$gitCount"
$originalGitKey = [Environment]::GetEnvironmentVariable($gitKeyName, 'Process')
$originalGitValue = [Environment]::GetEnvironmentVariable($gitValueName, 'Process')
try {
[Environment]::SetEnvironmentVariable($gitKeyName, 'safe.directory', 'Process')
[Environment]::SetEnvironmentVariable($gitValueName, $projectRoot.Replace('\','/'), 'Process')
[Environment]::SetEnvironmentVariable('GIT_CONFIG_COUNT', ($gitCount + 1).ToString(), 'Process')
Push-Location -LiteralPath $projectRoot
try {
    if (!(Test-Path -LiteralPath '.git')) { Invoke-PublishGit init --initial-branch=main; if ($LASTEXITCODE -ne 0) { throw 'Git initialization failed.' } }
    $selection = @('.gitattributes','.gitignore','.github','AGENTS.md','CONTRIBUTING.md','LICENSE','README.md','UPSTREAM.md','windows')
    foreach ($launcher in @('Collect-CI-Logs.cmd','Sync-CI-Fix.cmd','Publish-Release.cmd')) { if (Test-Path -LiteralPath $launcher) { $selection += $launcher } }
    if (Test-Path -LiteralPath 'docs') { $selection += 'docs' }
    Invoke-PublishGit add -- @selection
    if ($LASTEXITCODE -ne 0) { throw 'Source staging failed.' }
    $files = @(Invoke-PublishGit diff --cached --name-only)
    if (@($files | Where-Object { $_ -match '(^|/)(artifacts|\.tools|bin|obj)(/|$)|(^|/)(auth\.json|settings\.json|remote-secrets\.bin|history\.sqlite3)|\.pfx$|\.p12$|private[^/]*\.xml$' }).Count) { throw 'Local data or private files entered the source selection.' }
    $credentialFiles = @(Invoke-PublishGit grep --cached -l -E 'github_pat_[A-Za-z0-9_]{20,}|gh[pousr]_[A-Za-z0-9]{20,}|sk-proj-[A-Za-z0-9_-]{20,}|BEGIN (RSA |EC |OPENSSH )?PRIVATE KEY' -- .)
    if ($LASTEXITCODE -gt 1) { throw 'Source credential scan failed.' }
    if ($credentialFiles.Count) { throw ('Credential-like content found in: ' + ($credentialFiles -join ', ')) }
    Invoke-PublishGit diff --cached --check
    if ($LASTEXITCODE -ne 0) { throw 'Staged whitespace validation failed.' }
    $artifactRoot = Join-Path $projectRoot 'windows/artifacts'
    [IO.Directory]::CreateDirectory($artifactRoot) | Out-Null
    $tree = (Invoke-PublishGit write-tree).Trim()
    if ($LASTEXITCODE -ne 0) { throw 'Could not create the source tree.' }
    $sourceArchive = Join-Path $artifactRoot 'CodexMeter-Windows-source.zip'
    Invoke-PublishGit archive --format=zip --output=$sourceArchive $tree
    if ($LASTEXITCODE -ne 0) { throw 'Source archive failed.' }
    Write-Output "Prepared source: $sourceArchive"
    Write-Output ('Tracked source files: ' + @(Invoke-PublishGit ls-files).Count)
    if ($PrepareOnly) { return }
    $gh = Get-Command gh -ErrorAction SilentlyContinue
    $ghPath = if ($gh) { $gh.Source } else { & (Join-Path $PSScriptRoot 'setup-github-cli.ps1') }
    if (!$ghPath -or !(Test-Path -LiteralPath $ghPath)) { throw 'GitHub CLI is unavailable. Prepared source remains available.' }
    Invoke-PublishNative -FilePath $ghPath -Arguments @('auth','status','--hostname','github.com') -Quiet
    if ($LASTEXITCODE -ne 0) {
        Invoke-PublishNative -FilePath $ghPath -Arguments @('auth','login','--web','--hostname','github.com','--git-protocol','https')
        if ($LASTEXITCODE -ne 0) { throw 'GitHub browser authorization did not complete.' }
    }
    $identity = (Invoke-PublishNative -FilePath $ghPath -Arguments @('api','user') -Capture | ConvertFrom-Json)
    if ($LASTEXITCODE -ne 0 -or !$identity.login) { throw 'Cannot identify the authenticated GitHub account.' }
    if (!$Owner) { $Owner = $identity.login }
    if ($Owner -notmatch '^[A-Za-z0-9][A-Za-z0-9-]*$') { throw 'Invalid repository owner.' }
    $repository = "$Owner/$RepositoryName"
    $remote = "https://github.com/$repository.git"
    $origin = if (@(Invoke-PublishGit remote) -contains 'origin') { Invoke-PublishGit remote get-url origin } else { $null }
    if ($origin -and $origin -ne $remote) { throw 'Origin already points elsewhere. No remote was changed.' }
    if (!(Invoke-PublishGit config --get user.name)) { Invoke-PublishGit config --local user.name $identity.login }
    if (!(Invoke-PublishGit config --get user.email)) { Invoke-PublishGit config --local user.email ($identity.id.ToString() + '+' + $identity.login + '@users.noreply.github.com') }
    Invoke-PublishGit diff --cached --quiet
    if ($LASTEXITCODE -eq 1) { Invoke-PublishGit commit -m 'Complete Windows quota companion, recovery tests and GitHub release automation'; if ($LASTEXITCODE -ne 0) { throw 'Commit failed.' } }
    Invoke-PublishNative -FilePath $ghPath -Arguments @('repo','view',$repository,'--json','nameWithOwner') -Quiet
    $exists = $LASTEXITCODE -eq 0
    if ($exists -and !$origin) { throw 'That repository already exists; choose a new repository name. Nothing was pushed.' }
    if (!$exists) {
        Invoke-PublishNative -FilePath $ghPath -Arguments @('repo','create',$repository,"--$Visibility",'--source','.','--remote','origin','--description','Windows taskbar companion for Codex quota, history and optional notifications. Independent adaptation of raycalrui/CodexMeter.')
        if ($LASTEXITCODE -ne 0) { throw 'Repository creation failed.' }
    }
    Invoke-PublishNative -FilePath $ghPath -Arguments @('auth','setup-git','--hostname','github.com')
    if ($LASTEXITCODE -ne 0) { throw 'GitHub Git authentication setup failed.' }
    Write-Output "Pushing main to $remote"
    Invoke-PublishGit push --set-upstream origin main
    if ($LASTEXITCODE -ne 0) { throw ("Push failed (Git exit code $LASTEXITCODE). $publishNativeError`nThe local commit and remote are retained for retry.") }
    Invoke-PublishNative -FilePath $ghPath -Arguments @('repo','view',$repository,'--json','url','--jq','.url')
    if ($LASTEXITCODE -ne 0) { throw 'Repository URL verification failed.' }
} finally { Pop-Location }
} finally {
    [Environment]::SetEnvironmentVariable('GIT_CONFIG_COUNT', $originalGitCount, 'Process')
    [Environment]::SetEnvironmentVariable($gitKeyName, $originalGitKey, 'Process')
    [Environment]::SetEnvironmentVariable($gitValueName, $originalGitValue, 'Process')
}
