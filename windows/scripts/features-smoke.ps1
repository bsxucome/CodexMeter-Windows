param([string]$AppPath = (Join-Path (Split-Path $PSScriptRoot -Parent) "artifacts/CodexMeter-framework-x64/CodexMeter.exe"))
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes
function Find-OwnedWindow([int]$OwnerId, [string]$Name) {
    $condition = New-Object System.Windows.Automation.AndCondition(
        (New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::ProcessIdProperty, $OwnerId)),
        (New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::NameProperty, $Name)))
    for ($attempt = 0; $attempt -lt 60; $attempt++) {
        $result = [System.Windows.Automation.AutomationElement]::RootElement.FindFirst([System.Windows.Automation.TreeScope]::Children, $condition)
        if ($result) { return $result }
        Start-Sleep -Milliseconds 100
    }
    throw "Owned window missing: $Name"
}
function Find-Control($Window, [string]$Name) {
    $condition = New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::NameProperty, $Name)
    for ($attempt = 0; $attempt -lt 40; $attempt++) {
        $result = $Window.FindFirst([System.Windows.Automation.TreeScope]::Descendants, $condition)
        if ($result) { return $result }
        Start-Sleep -Milliseconds 100
    }
    throw "Control missing: $Name"
}
function Click($Window, [string]$Name) {
    (Find-Control $Window $Name).GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke()
}
function Choose($Window, [string]$Id, [string]$Value) {
    $condition = New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::AutomationIdProperty, $Id)
    $combo = $Window.FindFirst([System.Windows.Automation.TreeScope]::Descendants, $condition)
    if (!$combo) { throw "Combo missing: $Id" }
    $expansion = $combo.GetCurrentPattern([System.Windows.Automation.ExpandCollapsePattern]::Pattern)
    $expansion.Expand()
    Start-Sleep -Milliseconds 150
    (Find-Control $combo $Value).GetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern).Select()
    $expansion.Collapse()
}
# All new checks use preview; never launch a production instance or touch its database.
$owned = Start-Process -FilePath $AppPath -ArgumentList '--preview','--dark','--history' -WindowStyle Hidden -PassThru
try {
    $history = Find-OwnedWindow $owned.Id 'CodexMeter for Windows · 历史记录'
    if ((Find-Control $history '导出 CSV…').Current.IsEnabled) { throw 'Preview export must be disabled.' }
    if ((Find-Control $history '清除当前账号记录…').Current.IsEnabled) { throw 'Preview clear must be disabled.' }
    $history.GetCurrentPattern([System.Windows.Automation.WindowPattern]::Pattern).Close()
    Write-Output 'PASS history opens independently and preview cannot export or clear'
    $details = Find-OwnedWindow $owned.Id 'CodexMeter for Windows'
    Click $details '设置'
    $settings = Find-OwnedWindow $owned.Id 'CodexMeter for Windows · 设置'
    if ((Find-Control $settings '测试通知').Current.IsEnabled -or (Find-Control $settings '打开 Windows 启动应用设置').Current.IsEnabled) { throw 'Preview must not send notifications or open system startup settings.' }
    $sectionOrder = $settings.FindFirst([System.Windows.Automation.TreeScope]::Descendants,
        (New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::AutomationIdProperty, 'SectionOrder')))
    (Find-Control $sectionOrder 'Token 活动').GetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern).Select()
    for ($move = 0; $move -lt 4; $move++) { Click $settings '区域上移' }
    (Find-Control $settings '额度余额').GetCurrentPattern([System.Windows.Automation.TogglePattern]::Pattern).Toggle()
    (Find-Control $settings '启用本地通知').GetCurrentPattern([System.Windows.Automation.TogglePattern]::Pattern).Toggle()
    (Find-Control $settings '登录 Windows 时启动').GetCurrentPattern([System.Windows.Automation.TogglePattern]::Pattern).Toggle()
    (Find-Control $settings '低额度阈值').GetCurrentPattern([System.Windows.Automation.RangeValuePattern]::Pattern).SetValue(12)
    Click $settings '保存设置'
    $details = Find-OwnedWindow $owned.Id 'CodexMeter for Windows'
    $tokenTitle = Find-Control $details 'Token 活动'
    $quotaTitle = Find-Control $details '5 小时'
    if ($tokenTitle.Current.BoundingRectangle.Top -ge $quotaTitle.Current.BoundingRectangle.Top) { throw 'Saved section order was not applied.' }
    $null = Find-Control $details '可用次数：2'
    $balanceCondition = New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::NameProperty, '额度余额')
    if ($details.FindFirst([System.Windows.Automation.TreeScope]::Descendants, $balanceCondition)) { throw 'Hidden credit balance remains in the popup.' }
    Click $details '设置'
    $settings = Find-OwnedWindow $owned.Id 'CodexMeter for Windows · 设置'
    foreach ($name in @('启用本地通知','登录 Windows 时启动')) {
        if ((Find-Control $settings $name).GetCurrentPattern([System.Windows.Automation.TogglePattern]::Pattern).Current.ToggleState -ne [System.Windows.Automation.ToggleState]::On) { throw 'New preview preferences were not retained.' }
    }
    $null = Find-Control $settings '低额度阈值：12%'
    Write-Output 'PASS section ordering, optional balances/resets and isolated notification/startup preferences'
    Choose $settings 'IndicatorStyle' '双横条与百分比'
    Choose $settings 'Language' 'English'
    (Find-Control $settings 'Token 活动与图表').GetCurrentPattern([System.Windows.Automation.TogglePattern]::Pattern).Toggle()
    Click $settings '保存设置'
    Start-Sleep -Milliseconds 300
    $meter = Find-OwnedWindow $owned.Id 'CodexMeter for Windows Taskbar quota'
    if ($meter.Current.AutomationId -ne 'IndicatorStyle.Horizontal') { throw 'Taskbar style did not update.' }
    $details = Find-OwnedWindow $owned.Id 'CodexMeter for Windows'
    $tokenCondition = New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::NameProperty, 'Token activity')
    if ($details.FindFirst([System.Windows.Automation.TreeScope]::Descendants, $tokenCondition)) { throw 'Hidden token content remains visible.' }
    Click $details 'Settings'
    $settings = Find-OwnedWindow $owned.Id 'CodexMeter for Windows · Settings'
    Write-Output 'PASS language, taskbar style and popover content update without restart'
    foreach ($choice in @(@('Stacked bars and percentages','Stacked'), @('Percentage','Percentage'), @('Progress only','Progress'), @('Rings and percentage','Rings'))) {
        Choose $settings 'IndicatorStyle' $choice[0]
        Click $settings 'Save settings'
        Start-Sleep -Milliseconds 150
        $meter = Find-OwnedWindow $owned.Id 'CodexMeter for Windows Taskbar quota'
        if ($meter.Current.AutomationId -ne ('IndicatorStyle.' + $choice[1])) { throw 'Indicator style mismatch.' }
        Click (Find-OwnedWindow $owned.Id 'CodexMeter for Windows') 'Settings'
        $settings = Find-OwnedWindow $owned.Id 'CodexMeter for Windows · Settings'
    }
    Write-Output 'PASS all five indicator styles switch through settings'
    Choose $settings 'Language' '繁體中文'
    Click $settings 'Save settings'
    Click (Find-OwnedWindow $owned.Id 'CodexMeter for Windows') '設置'
    $settings = Find-OwnedWindow $owned.Id 'CodexMeter for Windows · 設置'
    $settings.GetCurrentPattern([System.Windows.Automation.WindowPattern]::Pattern).Close()
    Click (Find-OwnedWindow $owned.Id 'CodexMeter for Windows') '退出'
    if (!$owned.WaitForExit(5000)) { throw 'Feature preview failed to exit.' }
    Write-Output 'PASS traditional Chinese and clean exit'
} finally {
    $owned.Refresh()
    if (!$owned.HasExited) { $owned.Kill() }
}
