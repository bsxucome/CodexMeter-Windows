param([string]$AppPath = (Join-Path (Split-Path $PSScriptRoot -Parent) 'artifacts/CodexMeter-framework-x64/CodexMeter.exe'))
$ErrorActionPreference = 'Stop'
$appDirectory = Split-Path ([IO.Path]::GetFullPath($AppPath)) -Parent
$destination = Join-Path (Split-Path $PSScriptRoot -Parent) 'artifacts/preview-0.7.0'
[IO.Directory]::CreateDirectory($destination) | Out-Null
foreach ($mode in @('panel','settings','remote','data','about','history','appearance')) {
    foreach ($theme in @('light','dark')) {
        $arguments = @('--smoke')
        if ($mode -ne 'panel') { $arguments += "--$mode" }
        if ($theme -eq 'dark') { $arguments += '--dark' }
        $owned = Start-Process -FilePath $AppPath -ArgumentList $arguments -WindowStyle Hidden -PassThru
        try {
            if (!$owned.WaitForExit(15000)) { throw "Capture timed out: $mode $theme" }
            if ($owned.ExitCode -ne 0) { throw "Capture failed: $mode $theme" }
            $imageName = if ($mode -eq 'panel') { "$theme.png" } else { "$mode-$theme.png" }
            $source = Join-Path $appDirectory "screenshots/$imageName"
            Copy-Item -LiteralPath $source -Destination (Join-Path $destination "$mode-$theme.png") -Force
            Write-Output "PASS $mode $theme capture"
        } finally { $owned.Refresh(); if (!$owned.HasExited) { $owned.Kill() } }
    }
}
