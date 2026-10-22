param()
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'actions-release-common.ps1')
$projectRoot = Split-Path (Split-Path $PSScriptRoot -Parent) -Parent
$fixtureRoot = [IO.Path]::GetFullPath((Join-Path $projectRoot 'windows/artifacts/test-data'))
$fixture = [IO.Path]::GetFullPath((Join-Path $fixtureRoot ('actions-sign-' + [Guid]::NewGuid().ToString('N'))))
$names = @('CODEXMETER_RELEASE_KEY_XML','RELEASE_TAG','BUILD_RUNTIME','REPOSITORY')
$saved = @{}; foreach ($name in $names) { $saved[$name] = [Environment]::GetEnvironmentVariable($name, 'Process') }
$rsa = $null
function Run-SigningStep([bool]$ExpectSuccess) {
    $arguments = @('-NoProfile','-ExecutionPolicy','Bypass','-File', ('"' + (Join-Path $fixture 'workflow-step.ps1') + '"'))
    $owned = Start-Process powershell -ArgumentList $arguments -WorkingDirectory $fixture -WindowStyle Hidden -PassThru -Wait -RedirectStandardOutput (Join-Path $fixture 'stdout.log') -RedirectStandardError (Join-Path $fixture 'stderr.log')
    try { $exitCode = $owned.ExitCode } finally { $owned.Dispose() }
    if (($exitCode -eq 0) -ne $ExpectSuccess) { throw ('Unexpected signing step exit: ' + $exitCode) }
}
try {
    foreach ($directory in @('windows/scripts','windows/CodexMeter.Core','windows/CodexMeter.Windows','windows/artifacts','package')) { [IO.Directory]::CreateDirectory((Join-Path $fixture $directory)) | Out-Null }
    [IO.File]::WriteAllText((Join-Path $fixture '.fixture'), 'CodexMeter.ActionsSigningFixture.v1')
    foreach ($name in @('sign-actions-release.ps1','sign-release.ps1','actions-release-common.ps1')) { Copy-Item -LiteralPath (Join-Path $PSScriptRoot $name) -Destination (Join-Path $fixture 'windows/scripts') }
    foreach ($name in @('windows/CodexMeter.Core/ProductVersion.cs','windows/CodexMeter.Windows/CodexMeter.Windows.csproj','windows/CodexMeter.Windows/app.manifest')) { Copy-Item -LiteralPath (Join-Path $projectRoot $name) -Destination (Join-Path $fixture $name) }
    $steps = @(Get-ActionsPowerShellSteps (Join-Path $projectRoot '.github/workflows/windows.yml') | Where-Object { $_.Text.Trim() -eq './windows/scripts/sign-actions-release.ps1' })
    if ($steps.Count -ne 1) { throw 'The actual signing workflow must invoke the tested script exactly once.' }
    [IO.File]::WriteAllText((Join-Path $fixture 'workflow-step.ps1'), "`$ErrorActionPreference = 'Stop'`n" + $steps[0].Text, (New-Object Text.UTF8Encoding($false)))
    $legacy = "if (`$true) { Write-Output '未配置更新签名密钥，仅发布便携包。'; exit 0 }`n`$channel = 'stable'"
    $mojibake = [Text.Encoding]::GetEncoding(1252).GetString([Text.Encoding]::UTF8.GetBytes($legacy))
    $tokens = $null; $errors = $null
    $null = [Management.Automation.Language.Parser]::ParseInput($mojibake, [ref]$tokens, [ref]$errors)
    if (!$errors.Count) { throw 'The reported Windows PowerShell ANSI decoding failure was not reproduced.' }
    Write-Output 'PASS old UTF-8 inline localized script reproduces ANSI parser failure'
    $version = Read-ActionsProductVersion $fixture
    $env:RELEASE_TAG = 'windows-v' + $version; $env:REPOSITORY = 'fixture/Repo'; $env:CODEXMETER_RELEASE_KEY_XML = $null
    foreach ($runtime in @('Framework','DotNet')) {
        $env:BUILD_RUNTIME = $runtime; Run-SigningStep $true
        if (@(Get-ChildItem -LiteralPath (Join-Path $fixture 'windows/artifacts') -Recurse -File).Count) { throw 'Unsigned mode generated feed files.' }
        Write-Output ('PASS actual BOM-less workflow invocation: no-key ' + $runtime)
    }
    $env:RELEASE_TAG = 'windows-v0.0.0'; Run-SigningStep $false; $env:RELEASE_TAG = 'windows-v' + $version
    $env:BUILD_RUNTIME = 'Invalid'; Run-SigningStep $false; $env:BUILD_RUNTIME = 'Framework'
    $env:REPOSITORY = '../invalid'; Run-SigningStep $false; $env:REPOSITORY = 'fixture/Repo'
    Write-Output 'PASS wrong tag, runtime and repository fail even without a signing key'
    foreach ($name in @('CodexMeter.exe','CodexMeter.Core.dll')) { [IO.File]::WriteAllText((Join-Path $fixture ('package/' + $name)), 'fixture only') }
    $rsa = New-Object Security.Cryptography.RSACryptoServiceProvider -ArgumentList 2048; $rsa.PersistKeyInCsp = $false
    $env:CODEXMETER_RELEASE_KEY_XML = $rsa.ToXmlString($true)
    foreach ($channel in @('stable','beta')) {
        $selectedVersion = if ($channel -eq 'beta') { $version + '-beta.1' } else { $version }
        foreach ($name in @('windows/CodexMeter.Core/ProductVersion.cs','windows/CodexMeter.Windows/CodexMeter.Windows.csproj')) {
            $text = [IO.File]::ReadAllText((Join-Path $projectRoot $name)).Replace(('"' + $version + '"'), ('"' + $selectedVersion + '"')).Replace(('>' + $version + '<'), ('>' + $selectedVersion + '<'))
            [IO.File]::WriteAllText((Join-Path $fixture $name), $text)
        }
        $env:RELEASE_TAG = 'windows-v' + $selectedVersion
        foreach ($runtime in @('Framework','DotNet')) {
            $env:BUILD_RUNTIME = $runtime
            $suffix = if ($runtime -eq 'Framework') { 'framework-x64' } else { 'dotnet10-x64' }
            $archive = Join-Path $fixture ("windows/artifacts/CodexMeter-Windows-$selectedVersion-$suffix.zip")
            $packageFiles = @((Join-Path $fixture 'package/CodexMeter.exe'))
            if ($runtime -eq 'Framework') { $packageFiles += Join-Path $fixture 'package/CodexMeter.Core.dll' }
            Compress-Archive -LiteralPath $packageFiles -DestinationPath $archive
            Run-SigningStep $true
            $envelope = ConvertFrom-Json -InputObject ([IO.File]::ReadAllText((Join-Path $fixture "windows/artifacts/$channel-$suffix.json")))
            $bytes = [Convert]::FromBase64String($envelope.payload)
            if (!$rsa.VerifyData($bytes, [Security.Cryptography.CryptoConfig]::MapNameToOID('SHA256'), [Convert]::FromBase64String($envelope.signature))) { throw 'Workflow signed an unverifiable payload.' }
            $payload = ConvertFrom-Json -InputObject ([Text.Encoding]::UTF8.GetString($bytes))
            if ($payload.runtime -ne $suffix -or $payload.version -ne $selectedVersion -or $payload.channel -ne $channel -or !(Test-Path -LiteralPath (Join-Path $fixture "windows/artifacts/public-key-$suffix.json"))) { throw 'Workflow feed metadata or public key is incorrect.' }
            Write-Output ('PASS actual signing workflow: test-only RSA ' + $channel + '/' + $runtime)
            if ($runtime -eq 'DotNet') {
                $env:BUILD_RUNTIME = 'Framework'
                $frameworkArchive = Join-Path $fixture ("windows/artifacts/CodexMeter-Windows-$selectedVersion-framework-x64.zip")
                $backup = $frameworkArchive + '.fixture-backup'; Copy-Item -LiteralPath $frameworkArchive -Destination $backup
                try {
                    Copy-Item -LiteralPath $archive -Destination $frameworkArchive -Force
                    Run-SigningStep $false
                    Write-Output 'PASS Framework signing refuses a single-file package missing its core library'
                } finally { Copy-Item -LiteralPath $backup -Destination $frameworkArchive -Force; Remove-Item -LiteralPath $backup }
            }
        }
    }
} finally {
    if ($rsa) { $rsa.Dispose() }
    foreach ($name in $names) { [Environment]::SetEnvironmentVariable($name, $saved[$name], 'Process') }
    if ([IO.Path]::GetDirectoryName($fixture) -eq $fixtureRoot -and [IO.Path]::GetFileName($fixture) -match '^actions-sign-[a-f0-9]{32}$' -and (Test-Path -LiteralPath $fixture) -and [IO.File]::ReadAllText((Join-Path $fixture '.fixture')) -eq 'CodexMeter.ActionsSigningFixture.v1') { Remove-Item -LiteralPath $fixture -Recurse -Force }
}
