param()
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes
. (Join-Path $PSScriptRoot 'ui-automation-common.ps1')
$artifactRoot = [IO.Path]::GetFullPath((Join-Path (Split-Path $PSScriptRoot -Parent) 'artifacts/test-data'))
$fixture = [IO.Path]::GetFullPath((Join-Path $artifactRoot ('ui-ready-' + [Guid]::NewGuid().ToString('N'))))
[IO.Directory]::CreateDirectory($fixture) | Out-Null
[IO.File]::WriteAllText((Join-Path $fixture '.fixture'), 'CodexMeter.UiReadinessFixture.v1')
$owned = $null
try {
    $code = @'
using System;
using System.IO;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Threading;
public static class DelayedUiFixture {
    [STAThread] public static void Main(string[] args) {
        if (args.Length != 1 || File.ReadAllText(Path.Combine(args[0], ".fixture")) != "CodexMeter.UiReadinessFixture.v1") return;
        var body = new StackPanel();
        var window = new Window { Title = "CodexMeter UI readiness fixture", Width = 320, Height = 180, Content = body,
            ShowInTaskbar = false, ShowActivated = false, WindowStartupLocation = WindowStartupLocation.Manual, Left = -10000, Top = -10000 };
        DateTime? began = null;
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(100) };
        timer.Tick += delegate {
            if (!began.HasValue && File.Exists(Path.Combine(args[0], "begin-wait"))) began = DateTime.UtcNow;
            if (!began.HasValue || DateTime.UtcNow - began.Value < TimeSpan.FromSeconds(6)) return;
            var combo = new ComboBox(); combo.Items.Add("ready"); combo.SelectedIndex = 0;
            AutomationProperties.SetAutomationId(combo, "QuotaRange"); body.Children.Add(combo); timer.Stop();
        };
        timer.Start(); new Application().Run(window);
    }
}
'@
    $framework = Join-Path $env:WINDIR 'Microsoft.NET/Framework64/v4.0.30319'
    $wpf = Join-Path $framework 'WPF'
    $exe = Join-Path $fixture 'UiReady.exe'
    Add-Type -TypeDefinition $code -OutputAssembly $exe -OutputType WindowsApplication -ReferencedAssemblies @('System.dll', (Join-Path $wpf 'WindowsBase.dll'), (Join-Path $wpf 'PresentationCore.dll'), (Join-Path $wpf 'PresentationFramework.dll'), 'System.Xaml.dll', (Join-Path $wpf 'UIAutomationTypes.dll'))
    $owned = Start-Process -FilePath $exe -ArgumentList ('"' + $fixture + '"') -WindowStyle Hidden -PassThru
    $window = Owned-Window $owned.Id 'CodexMeter UI readiness fixture'
    [IO.File]::WriteAllText((Join-Path $fixture 'begin-wait'), 'fixture')
    $timer = [Diagnostics.Stopwatch]::StartNew()
    $control = Find-Element $window 'QuotaRange' -ById
    if ($timer.Elapsed.TotalSeconds -lt 5 -or $control.Current.ControlType -ne [System.Windows.Automation.ControlType]::ComboBox) { throw 'Delayed fixture did not exercise the former four-second deadline.' }
    Write-Output 'PASS real WPF provider exposes QuotaRange after a six-second delayed child load'
    $missing = $false
    try { $null = Find-Element $window 'NeverCreated' -ById -TimeoutSeconds 1 } catch { if ($_.Exception.Message -ne 'Owned element missing: NeverCreated') { throw }; $missing = $true }
    if (!$missing) { throw 'A missing control must still fail the readiness check.' }
    Write-Output 'PASS absent controls still fail; readiness does not skip assertions'
    $window.GetCurrentPattern([System.Windows.Automation.WindowPattern]::Pattern).Close()
    if (!$owned.WaitForExit(5000)) { throw 'Readiness fixture did not exit.' }
} finally {
    if ($owned) { $owned.Refresh(); if (!$owned.HasExited) { $owned.Kill() }; $owned.Dispose() }
    if ([IO.Path]::GetDirectoryName($fixture) -eq $artifactRoot -and [IO.Path]::GetFileName($fixture) -match '^ui-ready-[a-f0-9]{32}$' -and (Test-Path -LiteralPath $fixture) -and [IO.File]::ReadAllText((Join-Path $fixture '.fixture')) -eq 'CodexMeter.UiReadinessFixture.v1') { Remove-Item -LiteralPath $fixture -Recurse -Force }
}
