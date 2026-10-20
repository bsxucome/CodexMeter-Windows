param([string]$AppPath = (Join-Path (Split-Path $PSScriptRoot -Parent) 'artifacts/CodexMeter-framework-x64/CodexMeter.exe'))
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes
. (Join-Path $PSScriptRoot 'ui-automation-common.ps1')
function Press($Root, [string]$Value) { (Find-Element $Root $Value).GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke() }
function Select-Choice($Root, [string]$Id, [string]$Value) {
    $combo = Find-Element $Root $Id -ById
    $expansion = $combo.GetCurrentPattern([System.Windows.Automation.ExpandCollapsePattern]::Pattern)
    $expansion.Expand()
    Start-Sleep -Milliseconds 200
    (Find-Element $combo $Value).GetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern).Select()
    try { $expansion.Collapse() } catch [System.Windows.Automation.ElementNotAvailableException] { }
    Start-Sleep -Milliseconds 150
}
$owned = Start-Process -FilePath $AppPath -ArgumentList '--preview','--dark','--history' -WindowStyle Hidden -PassThru
try {
    if (!$owned.WaitForInputIdle(15000)) { throw 'Advanced preview did not become input-idle before its readiness deadline.' }
    $history = Owned-Window $owned.Id 'CodexMeter · 历史记录'
    Select-Choice $history 'QuotaRange' '日历周'
    if ((Find-Element $history '下一期').Current.IsEnabled) { throw 'Calendar navigation crossed into a future week.' }
    if (!(Find-Element $history '上一期').Current.IsEnabled) { throw 'Previous retained week is not browsable.' }
    Press $history '上一期'
    if (!(Find-Element $history '下一期').Current.IsEnabled) { throw 'Cannot return from the previous calendar week.' }
    Press $history '回到本期'
    Select-Choice $history 'QuotaRange' '日历月'
    Press $history '上一期'
    if (!(Find-Element $history 'ObservedConsumption' -ById).Current.Name.Contains('消耗')) { throw 'Observed consumption summary missing.' }
    Select-Choice $history 'QuotaRange' '全部历史'
    if (!(Find-Element $history 'ObservedConsumption' -ById).Current.Name.Contains('至少')) { throw 'Incomplete history total must be a lower bound.' }
    Write-Output 'PASS calendar week/month navigation, future bounds and consumption lower bound'
    Select-Choice $history 'TokenRange' '最近 365 天'
    if (!(Find-Element $history 'TokenSummary' -ById).Current.Name.Contains('按周汇总')) { throw 'One-year tokens were not grouped weekly.' }
    Select-Choice $history 'TokenRange' '全部历史'
    if (!(Find-Element $history 'TokenSummary' -ById).Current.Name.Contains('按月汇总')) { throw 'All-time tokens were not grouped monthly.' }
    Select-Choice $history 'QuotaRange' '当前重置周期'
    $null = Find-Element $history 'ExhaustionEstimate' -ById
    Write-Output 'PASS independent token grouping and eligible exhaustion estimate'
    $history.GetCurrentPattern([System.Windows.Automation.WindowPattern]::Pattern).Close()
    $details = Owned-Window $owned.Id 'CodexMeter'
    $null = Find-Element $details '每周额度历史'
    $widthBefore = (Owned-Window $owned.Id 'CodexMeter 任务栏额度').Current.BoundingRectangle.Width
    Press $details '设置'
    $settings = Owned-Window $owned.Id 'CodexMeter · 设置'
    Press $settings '高级外观…'
    $appearance = Owned-Window $owned.Id 'CodexMeter · 高级外观'
    Press $appearance '醒目'
    $ring = Find-Element $appearance '双环尺寸'
    $rangeValue = $ring.GetCurrentPattern([System.Windows.Automation.RangeValuePattern]::Pattern)
    if ($rangeValue.Current.Value -ne 22) { throw 'Bold appearance preset did not set ring size.' }
    $rangeValue.SetValue(24)
    Press $appearance '低额度'
    $null = Find-Element $appearance '12%'
    Press $appearance '应用到设置'
    (Find-Element $settings '每周额度小图').GetCurrentPattern([System.Windows.Automation.TogglePattern]::Pattern).Toggle()
    Press $settings '保存设置'
    Start-Sleep -Milliseconds 250
    $widthAfter = (Owned-Window $owned.Id 'CodexMeter 任务栏额度').Current.BoundingRectangle.Width
    if ($widthAfter -le $widthBefore) { throw 'Saved appearance did not resize the actual indicator.' }
    $condition = New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::NameProperty, '每周额度历史')
    if ($details.FindFirst([System.Windows.Automation.TreeScope]::Descendants, $condition)) { throw 'Weekly chart visibility did not update.' }
    Press $details '退出'
    if (!$owned.WaitForExit(5000)) { throw 'Advanced preview failed to quit.' }
    Write-Output 'PASS isolated appearance presets/sliders apply to the indicator; weekly chart toggles'
} finally {
    $owned.Refresh()
    if (!$owned.HasExited) { $owned.Kill() }
}
