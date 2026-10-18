param([string]$AppPath = (Join-Path (Split-Path $PSScriptRoot -Parent) "artifacts/CodexMeter-framework-x64/CodexMeter.exe"))
$ErrorActionPreference = "Stop"
Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes
$existing = @(Get-Process -Name CodexMeter -ErrorAction SilentlyContinue)
Add-Type @'
using System;
using System.Runtime.InteropServices;
public static class UiSmokeNative {
    [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr window);
}
'@
function Find-Window([int]$Id, [string]$Name) {
    $condition = New-Object System.Windows.Automation.AndCondition(
        (New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::ProcessIdProperty, $Id)),
        (New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::NameProperty, $Name)))
    for ($attempt = 0; $attempt -lt 60; $attempt++) {
        $window = [System.Windows.Automation.AutomationElement]::RootElement.FindFirst([System.Windows.Automation.TreeScope]::Children, $condition)
        if ($window) { return $window }
        Start-Sleep -Milliseconds 100
    }
    throw "Expected owned application window was not found."
}
function Invoke-Button($Window, [string]$Name) {
    $condition = New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::NameProperty, $Name)
    $button = $null
    for ($attempt = 0; $attempt -lt 40; $attempt++) {
        $button = $Window.FindFirst([System.Windows.Automation.TreeScope]::Descendants, $condition)
        if ($button) { break }
        Start-Sleep -Milliseconds 100
    }
    if (!$button) { throw "Expected button was not found." }
    $pattern = $button.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern)
    $pattern.Invoke()
}
$fixtureRoot = [IO.Path]::GetFullPath((Join-Path (Split-Path $PSScriptRoot -Parent) 'artifacts/test-data'))
$fixture = [IO.Path]::GetFullPath((Join-Path $fixtureRoot ('instance-process-' + [Guid]::NewGuid().ToString('N'))))
[IO.Directory]::CreateDirectory($fixture) | Out-Null
$fixtureArguments = @('--instance-fixture', ('"' + $fixture + '"'))
$owned = $null
try {
    # Reject unmarked fixture paths before any UI, CLI or production storage opens.
    [IO.File]::WriteAllText((Join-Path $fixture '.fixture'), 'invalid')
    $invalid = Start-Process -FilePath $AppPath -ArgumentList $fixtureArguments -WindowStyle Hidden -PassThru
    if (!$invalid.WaitForExit(5000)) { $invalid.Kill(); throw 'Invalid fixture did not exit.' }
    $invalid.Refresh()
    if ($invalid.ExitCode -ne 2) { throw 'Invalid instance fixture was accepted.' }
    [IO.File]::WriteAllText((Join-Path $fixture '.fixture'), 'CodexMeter.InstanceFixture.v1')
    $owned = Start-Process -FilePath $AppPath -ArgumentList $fixtureArguments -WindowStyle Hidden -PassThru
    $details = Find-Window $owned.Id "CodexMeter"
    Invoke-Button $details "设置"
    $preferences = Find-Window $owned.Id "CodexMeter · 设置"
    $preferences.GetCurrentPattern([System.Windows.Automation.WindowPattern]::Pattern).Close()
    Write-Output "PASS settings opens and closes through accessibility controls"
    # Opening Settings may dismiss the popover. Reopen through the real indicator.
    $meter = Find-Window $owned.Id 'CodexMeter 任务栏额度'
    $meter.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke()
    $details = Find-Window $owned.Id "CodexMeter"
    $handle = [IntPtr]$details.Current.NativeWindowHandle
    $details.GetCurrentPattern([System.Windows.Automation.WindowPattern]::Pattern).Close()
    Start-Sleep -Milliseconds 300
    if ([UiSmokeNative]::IsWindowVisible($handle)) { throw 'Closing details did not hide the popover.' }
    $owned.Refresh()
    if ($owned.HasExited) { throw "Closing details incorrectly exited the application." }
    $second = Start-Process -FilePath $AppPath -ArgumentList $fixtureArguments -WindowStyle Hidden -PassThru
    if (!$second.WaitForExit(5000)) { $second.Kill(); throw "Second instance failed to exit." }
    $second.Refresh()
    if ($second.ExitCode -ne 0) { throw "Second instance returned an error." }
    $details = Find-Window $owned.Id "CodexMeter"
    if (![UiSmokeNative]::IsWindowVisible($handle)) { throw 'Second instance did not show the existing popover.' }
    Write-Output "PASS isolated second instance restores existing details without production state"
    Invoke-Button $details "退出"
    if (!$owned.WaitForExit(5000)) { throw "Quit failed to terminate the owned application." }
    $owned.Refresh()
    if ($owned.ExitCode -ne 0) { throw "Tray application exited with an error." }
    Write-Output "PASS isolated instance quits cleanly"
} finally {
    if ($owned) {
        $owned.Refresh()
        if (!$owned.HasExited) { $owned.Kill(); $null = $owned.WaitForExit(5000) }
    }
    if ([IO.Path]::GetDirectoryName($fixture) -eq $fixtureRoot -and [IO.Path]::GetFileName($fixture) -match '^instance-process-[a-f0-9]{32}$' -and (Test-Path -LiteralPath $fixture)) {
        Remove-Item -LiteralPath $fixture -Recurse -Force
    }
}
foreach ($process in $existing) {
    $process.Refresh()
    if ($process.HasExited) { throw 'An existing user instance exited during isolated UI checks.' }
}
# Preview mode isolates UI interaction from real accounts and settings storage.
$preview = Start-Process -FilePath $AppPath -ArgumentList "--preview","--dark" -WindowStyle Hidden -PassThru
try {
    $details = Find-Window $preview.Id "CodexMeter"
    $meter = Find-Window $preview.Id "CodexMeter 任务栏额度"
    $texts = $meter.FindAll([System.Windows.Automation.TreeScope]::Descendants,
        (New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::ControlTypeProperty,
            [System.Windows.Automation.ControlType]::Text)))
    $hasPercent = $false
    foreach ($text in $texts) { if ($text.Current.Name -match '^\d+%$') { $hasPercent = $true } }
    if (!$hasPercent) { throw "Taskbar indicator does not expose its quota percentage." }
    Write-Output "PASS taskbar indicator displays live-format percentage in preview"
    Invoke-Button $details "设置"
    $preferences = Find-Window $preview.Id "CodexMeter · 设置"
    $dockingCondition = New-Object System.Windows.Automation.AndCondition(
        (New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::NameProperty, "任务栏位置")),
        (New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::ControlTypeProperty, [System.Windows.Automation.ControlType]::ComboBox)))
    $docking = $preferences.FindFirst([System.Windows.Automation.TreeScope]::Descendants, $dockingCondition)
    if (!$docking) { throw "Start docking choice missing." }
    $expand = $docking.GetCurrentPattern([System.Windows.Automation.ExpandCollapsePattern]::Pattern)
    $expand.Expand()
    Start-Sleep -Milliseconds 300
    $startLeft = $docking.FindFirst([System.Windows.Automation.TreeScope]::Descendants,
        (New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::NameProperty, "紧靠 Windows 开始按钮左侧")))
    if (!$startLeft) { throw "Start-left docking item missing." }
    $startLeft.GetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern).Select()
    $expand.Collapse()
    Write-Output "PASS themed dropdown selects Start-left docking"
    $toggle = $preferences.FindFirst([System.Windows.Automation.TreeScope]::Descendants,
        (New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::ControlTypeProperty,
            [System.Windows.Automation.ControlType]::CheckBox)))
    if (!$toggle) { throw "Taskbar visibility control missing." }
    $toggle.GetCurrentPattern([System.Windows.Automation.TogglePattern]::Pattern).Toggle()
    Invoke-Button $preferences "保存设置"
    Start-Sleep -Milliseconds 400
    $condition = New-Object System.Windows.Automation.AndCondition(
        (New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::ProcessIdProperty, $preview.Id)),
        (New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::NameProperty, "CodexMeter 任务栏额度")))
    $remaining = [System.Windows.Automation.AutomationElement]::RootElement.FindFirst([System.Windows.Automation.TreeScope]::Children, $condition)
    if ($remaining -and !$remaining.Current.IsOffscreen) { throw "Taskbar indicator remained visible after disabling it." }
    Write-Output "PASS taskbar visibility setting hides the indicator"
    Invoke-Button (Find-Window $preview.Id "CodexMeter") "退出"
    if (!$preview.WaitForExit(5000)) { throw "Preview did not quit." }
    Write-Output "PASS taskbar preview exits without background windows"
} finally {
    $preview.Refresh()
    if (!$preview.HasExited) { $preview.Kill() }
}
