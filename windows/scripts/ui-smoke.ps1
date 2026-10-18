param([string]$AppPath = (Join-Path (Split-Path $PSScriptRoot -Parent) "artifacts/CodexMeter-framework-x64/CodexMeter.exe"))
$ErrorActionPreference = "Stop"
Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes
$existing = @(Get-Process -Name CodexMeter -ErrorAction SilentlyContinue)
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
if ($existing.Count -eq 0) {
$owned = Start-Process -FilePath $AppPath -ArgumentList "--show" -WindowStyle Hidden -PassThru
try {
    $details = Find-Window $owned.Id "CodexMeter"
    Invoke-Button $details "设置"
    $preferences = Find-Window $owned.Id "CodexMeter · 设置"
    $preferences.GetCurrentPattern([System.Windows.Automation.WindowPattern]::Pattern).Close()
    Write-Output "PASS settings opens and closes through accessibility controls"
    $details = Find-Window $owned.Id "CodexMeter"
    $details.GetCurrentPattern([System.Windows.Automation.WindowPattern]::Pattern).Close()
    Start-Sleep -Milliseconds 300
    $owned.Refresh()
    if ($owned.HasExited) { throw "Closing details incorrectly exited the tray application." }
    $second = Start-Process -FilePath $AppPath -ArgumentList "--show" -WindowStyle Hidden -PassThru
    if (!$second.WaitForExit(5000)) { $second.Kill(); throw "Second instance failed to exit." }
    $second.Refresh()
    if ($second.ExitCode -ne 0) { throw "Second instance returned an error." }
    $details = Find-Window $owned.Id "CodexMeter"
    Write-Output "PASS second instance restores existing details"
    Invoke-Button $details "退出"
    if (!$owned.WaitForExit(5000)) { throw "Quit failed to terminate the owned application." }
    $owned.Refresh()
    if ($owned.ExitCode -ne 0) { throw "Tray application exited with an error." }
    Write-Output "PASS tray quit exits cleanly"
} finally {
    $owned.Refresh()
    if (!$owned.HasExited) { $owned.Kill() }
}

} else { Write-Output "SKIP normal-instance checks: user instance is running." }
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
