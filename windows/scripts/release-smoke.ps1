param([string]$AppPath = (Join-Path (Split-Path $PSScriptRoot -Parent) 'artifacts/CodexMeter-framework-x64/CodexMeter.exe'))
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes
function Owned([int]$OwnerId, [string]$Name) {
    $condition = New-Object System.Windows.Automation.AndCondition(
        (New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::ProcessIdProperty, $OwnerId)),
        (New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::NameProperty, $Name)))
    for ($attempt = 0; $attempt -lt 60; $attempt++) {
        $window = [System.Windows.Automation.AutomationElement]::RootElement.FindFirst([System.Windows.Automation.TreeScope]::Children, $condition)
        if ($window) { return $window }
        Start-Sleep -Milliseconds 100
    }
    throw "Missing owned window: $Name"
}
function Element($Root, [string]$Value, [switch]$ById) {
    $property = if ($ById) { [System.Windows.Automation.AutomationElement]::AutomationIdProperty } else { [System.Windows.Automation.AutomationElement]::NameProperty }
    $condition = New-Object System.Windows.Automation.PropertyCondition($property, $Value)
    $found = $Root.FindFirst([System.Windows.Automation.TreeScope]::Descendants, $condition)
    if (!$found) { throw "Missing element: $Value" }
    return $found
}
function Press($Root, [string]$Name) { (Element $Root $Name).GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke() }
function Close-Window($Root) { $Root.GetCurrentPattern([System.Windows.Automation.WindowPattern]::Pattern).Close(); Start-Sleep -Milliseconds 150 }
$owned = Start-Process -FilePath $AppPath -ArgumentList '--preview','--settings' -WindowStyle Hidden -PassThru
try {
    $settings = Owned $owned.Id 'CodexMeter · 设置'
    $width = $settings.Current.BoundingRectangle.Width
    if ($width -lt 375 -or $width -gt 575) { throw 'Settings width does not match the compact panel at 100-150% DPI.' }
    $save = Element $settings '保存设置'
    $before = $save.Current.BoundingRectangle
    $scroll = (Element $settings 'Compact.Scroll' -ById).GetCurrentPattern([System.Windows.Automation.ScrollPattern]::Pattern)
    if (!$scroll.Current.VerticallyScrollable) { throw 'Compact settings must remain scrollable.' }
    $scroll.SetScrollPercent(-1,100)
    Start-Sleep -Milliseconds 150
    if ([Math]::Abs($save.Current.BoundingRectangle.Top - $before.Top) -gt 1 -or $save.Current.IsOffscreen) { throw 'Settings footer moved with body scrolling.' }
    Write-Output 'PASS compact settings width, working body scroll and fixed save footer'
    Press $settings '远程补充提醒…'
    $remote = Owned $owned.Id 'CodexMeter · 远程补充提醒'
    foreach ($kind in @('Bark','Gotify','Telegram','Discord')) {
        if ((Element $remote "Remote.Test.$kind" -ById).Current.IsEnabled) { throw 'Preview may not send remote messages.' }
        if (!(Element $remote "Remote.Secret.$kind" -ById).Current.IsPassword) { throw 'Remote credential input must be masked.' }
        $null = Element $remote "Remote.FiveHour.$kind" -ById
        $null = Element $remote "Remote.Weekly.$kind" -ById
    }
    if ([Math]::Abs($remote.Current.BoundingRectangle.Width - $width) -gt 1) { throw 'Remote width differs from Settings.' }
    Close-Window $remote
    Write-Output 'PASS four isolated remote channels, masked credentials and disabled preview transmission'
    Press $settings '备份、恢复与账号管理…'
    $data = Owned $owned.Id 'CodexMeter · 历史与备份'
    foreach ($name in @('导出历史备份…','恢复历史备份…','清除全部账号历史…')) {
        if ((Element $data $name).Current.IsEnabled) { throw 'Preview must not access production history.' }
    }
    Close-Window $data
    Write-Output 'PASS history backup and all-account management are isolated in preview'
    Press $settings '关于与检查更新…'
    $about = Owned $owned.Id 'CodexMeter · 关于与更新'
    foreach ($name in @('Update.Check','Update.Download','Update.Install')) {
        if ((Element $about $name -ById).Current.IsEnabled) { throw 'Preview cannot check, download or install real updates.' }
    }
    $null = Element $about 'Update.Feed' -ById
    $null = Element $about 'Update.PublicKey' -ById
    Press $about '模拟有更新'
    $null = Element $about '发现新版本：9.9.9 · 演示数据'
    if ((Element $about 'Update.Install' -ById).Current.IsEnabled) { throw 'Simulated updates must never become installable.' }
    Press $about '模拟检查失败'
    $null = Element $about '检查失败；演示错误状态。'
    Press $about '模拟最新版本'
    $null = Element $about '当前已是此通道的最新版本。'
    if ([Math]::Abs($about.Current.BoundingRectangle.Width - $width) -gt 1) { throw 'About width differs from Settings.' }
    Close-Window $about
    Close-Window $settings
    Press (Owned $owned.Id 'CodexMeter') '退出'
    if (!$owned.WaitForExit(5000)) { throw 'Release preview failed to exit.' }
    Write-Output 'PASS configurable Windows update source/public key and preview install isolation'
} finally { $owned.Refresh(); if (!$owned.HasExited) { $owned.Kill() } }
