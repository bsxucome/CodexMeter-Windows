param(
    [string]$AppPath = (Join-Path (Split-Path $PSScriptRoot -Parent) "artifacts/CodexMeter-framework-x64/CodexMeter.exe"),
    [string]$BaselinePath = ""
)
$ErrorActionPreference = "Stop"
$windowsRoot = Split-Path $PSScriptRoot -Parent
$testRoot = Join-Path $windowsRoot "artifacts/indicator-input-tests"
[IO.Directory]::CreateDirectory($testRoot) | Out-Null
$framework = Join-Path $env:WINDIR "Microsoft.NET/Framework64/v4.0.30319"
$wpf = Join-Path $framework "WPF"
$compiler = Join-Path $framework "csc.exe"
$core = Join-Path (Split-Path $AppPath -Parent) "CodexMeter.Core.dll"
Copy-Item -LiteralPath $core -Destination $testRoot -Force
$probe = Join-Path $testRoot "IndicatorInput.Tests.exe"
& $compiler /nologo /codepage:65001 /langversion:5 /warnaserror+ /platform:x64 /define:INDICATOR_INPUT_TEST /target:exe "/out:$probe" "/r:$core" /r:System.Drawing.dll /r:System.Windows.Forms.dll /r:System.Xaml.dll "/r:$wpf/PresentationCore.dll" "/r:$wpf/PresentationFramework.dll" "/r:$wpf/WindowsBase.dll" (Join-Path $windowsRoot "CodexMeter.Tests/IndicatorInputProbe.cs")
if ($LASTEXITCODE -ne 0) { throw "Indicator input probe build failed." }
if ($BaselinePath) {
    & $probe $BaselinePath --expect-holes
    if ($LASTEXITCODE -ne 0) { throw "Expected baseline regression was not reproduced." }
}
& $probe $AppPath
if ($LASTEXITCODE -ne 0) { throw "Indicator input regression failed." }
& $probe $AppPath --shell-probe
if ($LASTEXITCODE -ne 0) { throw "Read-only shell probe failed." }

Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes
Add-Type @'
using System;
using System.Runtime.InteropServices;
public static class IndicatorSmokeNative {
    [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr window);
}
'@
function Find-OwnedWindow([int]$Id, [string]$Name) {
    $condition = New-Object System.Windows.Automation.AndCondition(
        (New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::ProcessIdProperty, $Id)),
        (New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::NameProperty, $Name)))
    for ($attempt = 0; $attempt -lt 50; $attempt++) {
        $window = [System.Windows.Automation.AutomationElement]::RootElement.FindFirst([System.Windows.Automation.TreeScope]::Children, $condition)
        if ($window -and !$window.Current.IsOffscreen) { return $window }
        Start-Sleep -Milliseconds 100
    }
    throw "Owned interaction-check window was not found: $Name"
}
$preview = Start-Process -FilePath $AppPath -ArgumentList "--preview","--interaction-check" -WindowStyle Hidden -PassThru
try {
    $indicator = Find-OwnedWindow $preview.Id "CodexMeter for Windows 任务栏额度"
    $invoke = $indicator.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern)
    for ($round = 0; $round -lt 5; $round++) {
        $invoke.Invoke()
        $details = Find-OwnedWindow $preview.Id "CodexMeter for Windows"
        Start-Sleep -Milliseconds 350
        $handle = [IntPtr]$details.Current.NativeWindowHandle
        if (![IndicatorSmokeNative]::IsWindowVisible($handle)) { throw "Details immediately dismissed after open." }
        if ($details.Current.BoundingRectangle.IntersectsWith($indicator.Current.BoundingRectangle)) { throw "Details cover the indicator." }
        $details.GetCurrentPattern([System.Windows.Automation.WindowPattern]::Pattern).Close()
        Start-Sleep -Milliseconds 200
        if ([IndicatorSmokeNative]::IsWindowVisible($handle)) { throw "Details failed to hide." }
        $indicator = Find-OwnedWindow $preview.Id "CodexMeter for Windows 任务栏额度"
        $preview.Refresh()
        if ($preview.HasExited) { throw "Application exited during repeated opening." }
    }
    Write-Output "PASS five real popup open/close cycles keep the indicator visible and do not overlap it"
    Write-Output "SKIP physical focus transitions: not simulated by these UI Automation checks."
    $invoke.Invoke()
    $details = Find-OwnedWindow $preview.Id "CodexMeter for Windows"
    $handle = [IntPtr]$details.Current.NativeWindowHandle
    Start-Sleep -Milliseconds 1300
    if (![IndicatorSmokeNative]::IsWindowVisible($handle)) { throw "Reopened details disappeared." }
    $null = Find-OwnedWindow $preview.Id "CodexMeter for Windows 任务栏额度"
    Write-Output "PASS details reopen after dismissal and survive the next placement tick"
} finally {
    $preview.Refresh()
    if (!$preview.HasExited) { $preview.Kill(); $null = $preview.WaitForExit(5000) }
}
