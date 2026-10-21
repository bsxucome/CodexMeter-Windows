param([string]$AppPath = (Join-Path (Split-Path $PSScriptRoot -Parent) 'artifacts/CodexMeter-framework-x64/CodexMeter.exe'))
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes
. (Join-Path $PSScriptRoot 'ui-automation-common.ps1')
function Press($Root, [string]$Name) { (Find-Element $Root $Name).GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke() }
function Close-Window($Root) { $Root.GetCurrentPattern([System.Windows.Automation.WindowPattern]::Pattern).Close(); Start-Sleep -Milliseconds 150 }
$owned = Start-Process -FilePath $AppPath -ArgumentList '--preview','--settings' -WindowStyle Hidden -PassThru
try {
    $settings = Owned-Window $owned.Id 'CodexMeter for Windows · 设置'
    $width = $settings.Current.BoundingRectangle.Width
    if ($width -lt 375 -or $width -gt 575) { throw 'Settings width does not match the compact panel at 100-150% DPI.' }
    $save = Find-Element $settings '保存设置'
    $before = $save.Current.BoundingRectangle
    $scroll = (Find-Element $settings 'Compact.Scroll' -ById).GetCurrentPattern([System.Windows.Automation.ScrollPattern]::Pattern)
    if (!$scroll.Current.VerticallyScrollable) { throw 'Compact settings must remain scrollable.' }
    $scroll.SetScrollPercent(-1,100)
    Start-Sleep -Milliseconds 150
    if ([Math]::Abs($save.Current.BoundingRectangle.Top - $before.Top) -gt 1 -or $save.Current.IsOffscreen) { throw 'Settings footer moved with body scrolling.' }
    Write-Output 'PASS compact settings width, working body scroll and fixed save footer'
    Press $settings '远程补充提醒…'
    $remote = Owned-Window $owned.Id 'CodexMeter for Windows · 远程补充提醒'
    foreach ($kind in @('Bark','Gotify','Telegram','Discord')) {
        if ((Find-Element $remote "Remote.Test.$kind" -ById).Current.IsEnabled) { throw 'Preview may not send remote messages.' }
        if (!(Find-Element $remote "Remote.Secret.$kind" -ById).Current.IsPassword) { throw 'Remote credential input must be masked.' }
        $null = Find-Element $remote "Remote.FiveHour.$kind" -ById
        $null = Find-Element $remote "Remote.Weekly.$kind" -ById
    }
    if ([Math]::Abs($remote.Current.BoundingRectangle.Width - $width) -gt 1) { throw 'Remote width differs from Settings.' }
    Close-Window $remote
    Write-Output 'PASS four isolated remote channels, masked credentials and disabled preview transmission'
    Press $settings '备份、恢复与账号管理…'
    $data = Owned-Window $owned.Id 'CodexMeter for Windows · 历史与备份'
    foreach ($name in @('导出历史备份…','恢复历史备份…','清除全部账号历史…')) {
        if ((Find-Element $data $name).Current.IsEnabled) { throw 'Preview must not access production history.' }
    }
    Close-Window $data
    Write-Output 'PASS history backup and all-account management are isolated in preview'
    Press $settings '关于与检查更新…'
    $about = Owned-Window $owned.Id 'CodexMeter for Windows · 关于与更新'
    foreach ($name in @('Update.Check','Update.Download','Update.Install')) {
        if ((Find-Element $about $name -ById).Current.IsEnabled) { throw 'Preview cannot check, download or install real updates.' }
    }
    $null = Find-Element $about 'Update.ProjectHome' -ById
    $null = Find-Element $about 'Update.Releases' -ById
    $feed = (Find-Element $about 'Update.Feed' -ById).GetCurrentPattern([System.Windows.Automation.ValuePattern]::Pattern)
    $expectedFeed = 'https://github.com/bsxucome/CodexMeter-for-Windows/releases/download/windows-update-feed/{channel}-{runtime}.json'
    Wait-ElementValue $about 'Update.Feed' $expectedFeed
    $feed.SetValue('https://example.test/custom.json')
    Wait-ElementValue $about 'Update.Feed' 'https://example.test/custom.json'
    (Find-Element $about 'Update.UseGitHub' -ById).GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke()
    Wait-ElementValue $about 'Update.Feed' $expectedFeed
    Write-Output 'PASS GitHub source action restores the exact manifest URL after the WPF action completes'
    $null = Find-Element $about 'Update.PublicKey' -ById
    Press $about '保存更新设置'
    $null = Find-Element $about '更新设置已保存。'
    if ((Find-Element $about 'Update.Check' -ById).Current.IsEnabled) { throw 'A prepared source without a public key must remain disabled.' }
    Press $about '模拟有更新'
    $null = Find-Element $about '发现新版本：9.9.9 · 演示数据'
    if ((Find-Element $about 'Update.Install' -ById).Current.IsEnabled) { throw 'Simulated updates must never become installable.' }
    Press $about '模拟检查失败'
    $null = Find-Element $about '检查失败；演示错误状态。'
    Press $about '模拟最新版本'
    $null = Find-Element $about '当前已是此通道的最新版本。'
    if ([Math]::Abs($about.Current.BoundingRectangle.Width - $width) -gt 1) { throw 'About width differs from Settings.' }
    Close-Window $about
    Close-Window $settings
    Press (Owned-Window $owned.Id 'CodexMeter for Windows') '退出'
    if (!$owned.WaitForExit(5000)) { throw 'Release preview failed to exit.' }
    Write-Output 'PASS configurable Windows update source/public key and preview install isolation'
} finally { $owned.Refresh(); if (!$owned.HasExited) { $owned.Kill(); if (!$owned.WaitForExit(5000)) { throw 'Release preview did not stop; owned process state is retained.' } }; $owned.Dispose() }
