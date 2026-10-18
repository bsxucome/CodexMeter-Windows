param()
$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path (Split-Path $PSScriptRoot -Parent) -Parent
$failures = @()
foreach ($script in Get-ChildItem -LiteralPath (Join-Path $projectRoot 'windows/scripts') -Filter '*.ps1') {
    $tokens = $null; $errors = $null
    $null = [Management.Automation.Language.Parser]::ParseFile($script.FullName, [ref]$tokens, [ref]$errors)
    foreach ($error in $errors) { $failures += $script.Name + ':' + $error.Extent.StartLineNumber + ': ' + $error.Message }
    if ([IO.File]::ReadAllText($script.FullName) -match '[\u4e00-\u9fff]') {
        $bytes = [IO.File]::ReadAllBytes($script.FullName)
        if ($bytes.Length -lt 3 -or $bytes[0] -ne 239 -or $bytes[1] -ne 187 -or $bytes[2] -ne 191) { $failures += $script.Name + ': Chinese PowerShell scripts require a UTF-8 BOM.' }
    }
}
if ($failures.Count) { throw ($failures -join "`n") }
Write-Output 'PASS PowerShell syntax and Windows PowerShell UTF-8 compatibility'
Push-Location -LiteralPath $projectRoot
try {
    git diff --check
    if ($LASTEXITCODE -ne 0) { throw 'Working tree whitespace check failed.' }
} finally { Pop-Location }
