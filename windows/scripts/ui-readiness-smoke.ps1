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
        const string source = "https://github.com/bsxucome/CodexMeter-for-Windows/releases/download/windows-update-feed/{channel}-{runtime}.json";
        var feed = new TextBox { Text = "https://example.test/custom.json" };
        AutomationProperties.SetAutomationId(feed, "Update.Feed"); body.Children.Add(feed);
        var restore = new Button { Content = "Restore fixture feed" }; body.Children.Add(restore);
        DateTime? responseBegan = null;
        var response = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(100) };
        restore.Click += delegate { responseBegan = null; response.Start(); };
        response.Tick += delegate {
            if (!File.Exists(Path.Combine(args[0], "release-response"))) return;
            if (!responseBegan.HasValue) responseBegan = DateTime.UtcNow;
            if (DateTime.UtcNow - responseBegan.Value < TimeSpan.FromMilliseconds(1200)) return;
            response.Stop();
            var replacement = new TextBox { Text = File.Exists(Path.Combine(args[0], "wrong-response")) ? source + ".wrong" : source };
            AutomationProperties.SetAutomationId(replacement, "Update.Feed");
            int index = body.Children.IndexOf(feed); body.Children.RemoveAt(index); body.Children.Insert(index, replacement); feed = replacement;
        };
        body.Children.Add(new TextBlock { Text = "CodexMeter delayed fixture window" });
        var viewport = new ScrollViewer { Content = body, VerticalScrollBarVisibility = ScrollBarVisibility.Hidden };
        AutomationProperties.SetAutomationId(viewport, "Fixture.Scroll");
        var window = new Window { Title = "CodexMeter UI readiness fixture", Width = 320, Height = 180, Content = viewport,
            ShowInTaskbar = false, ShowActivated = false, WindowStartupLocation = WindowStartupLocation.CenterScreen };
        DateTime? began = null, windowBegan = null;
        bool loaded = false, childCreated = false;
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(100) };
        timer.Tick += delegate {
            if (!began.HasValue && File.Exists(Path.Combine(args[0], "begin-wait"))) began = DateTime.UtcNow;
            if (!loaded && began.HasValue && DateTime.UtcNow - began.Value >= TimeSpan.FromSeconds(6)) {
                var combo = new ComboBox(); combo.Items.Add("ready"); combo.SelectedIndex = 0;
                AutomationProperties.SetAutomationId(combo, "QuotaRange"); body.Children.Add(combo);
                body.Children.Add(new Border { Height = 1200 });
                body.Children.Add(new Button { Content = "Scroll witness" }); loaded = true;
            }
            if (!windowBegan.HasValue && File.Exists(Path.Combine(args[0], "begin-window"))) { windowBegan = DateTime.UtcNow; File.WriteAllText(Path.Combine(args[0], "window-phase"), "handshake"); }
            if (!childCreated && windowBegan.HasValue && DateTime.UtcNow - windowBegan.Value >= TimeSpan.FromSeconds(1)) {
                var child = new Window { Title = "CodexMeter delayed fixture window", Width = 240, Height = 120,
                    Owner = window, Content = new TextBlock { Text = "owned window" }, ShowInTaskbar = false, ShowActivated = false };
                AutomationProperties.SetName(child, "CodexMeter delayed fixture window"); child.Show();
                childCreated = true;
                File.WriteAllText(Path.Combine(args[0], "window-phase"), "shown");
            }
            if (loaded && childCreated) timer.Stop();
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
    $expectedFeed = 'https://github.com/bsxucome/CodexMeter-for-Windows/releases/download/windows-update-feed/{channel}-{runtime}.json'
    $feed = (Find-Element $window 'Update.Feed' -ById).GetCurrentPattern([System.Windows.Automation.ValuePattern]::Pattern)
    $restore = (Find-Element $window 'Restore fixture feed').GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern)
    $restore.Invoke()
    if ($feed.Current.Value -eq $expectedFeed) { throw 'Delayed response fixture did not reproduce the former immediate-read race.' }
    $stalled = $false
    try { Wait-ElementValue $window 'Update.Feed' $expectedFeed -TimeoutSeconds 1 } catch { if ($_.Exception.Message -ne 'UI state not ready: value of Update.Feed') { throw }; $stalled = $true }
    if (!$stalled) { throw 'An action that has not applied its value must fail at the deadline.' }
    [IO.File]::WriteAllText((Join-Path $fixture 'release-response'), 'fixture')
    $responseTime = [Diagnostics.Stopwatch]::StartNew()
    Wait-ElementValue $window 'Update.Feed' $expectedFeed
    if ($responseTime.Elapsed.TotalMilliseconds -lt 1000) { throw 'Delayed response was accepted before the replacement value was applied.' }
    Write-Output 'PASS old immediate assertion fails while bounded value waiting survives a delayed invoke and replaced WPF provider'
    [IO.File]::WriteAllText((Join-Path $fixture 'wrong-response'), 'fixture')
    (Find-Element $window 'Update.Feed' -ById).GetCurrentPattern([System.Windows.Automation.ValuePattern]::Pattern).SetValue('https://example.test/custom.json')
    Wait-ElementValue $window 'Update.Feed' 'https://example.test/custom.json'
    $restore.Invoke()
    $wrong = $false
    try { Wait-ElementValue $window 'Update.Feed' $expectedFeed -TimeoutSeconds 2 } catch { if ($_.Exception.Message -ne 'UI state not ready: value of Update.Feed') { throw }; $wrong = $true }
    if (!$wrong) { throw 'A wrong URL sharing the expected prefix must fail.' }
    Wait-ElementValue $window 'Update.Feed' ($expectedFeed + '.wrong')
    Write-Output 'PASS wrong same-prefix URL still fails at the bounded deadline'
    $absentValue = $false
    try { Wait-ElementValue $window 'Absent.Feed' $expectedFeed -TimeoutSeconds 1 } catch { if ($_.Exception.Message -ne 'UI state not ready: value of Absent.Feed') { throw }; $absentValue = $true }
    if (!$absentValue) { throw 'A missing source field must fail.' }
    Write-Output 'PASS a missing source control still fails rather than skipping the value assertion'
    [IO.File]::WriteAllText((Join-Path $fixture 'begin-wait'), 'fixture')
    $timer = [Diagnostics.Stopwatch]::StartNew()
    $control = Find-Element $window 'QuotaRange' -ById
    if ($timer.Elapsed.TotalSeconds -lt 5 -or $control.Current.ControlType -ne [System.Windows.Automation.ControlType]::ComboBox) { throw 'Delayed fixture did not exercise the former four-second deadline.' }
    Write-Output 'PASS real WPF provider exposes QuotaRange after a six-second delayed child load'
    $missing = $false
    try { $null = Find-Element $window 'NeverCreated' -ById -TimeoutSeconds 1 } catch { if ($_.Exception.Message -ne 'Owned element missing: NeverCreated') { throw }; $missing = $true }
    if (!$missing) { throw 'A missing control must still fail the readiness check.' }
    Write-Output 'PASS absent controls still fail; readiness does not skip assertions'
    [IO.File]::WriteAllText((Join-Path $fixture 'begin-window'), 'fixture')
    $windowTimer = [Diagnostics.Stopwatch]::StartNew()
    $childWindow = Owned-Window $owned.Id 'CodexMeter delayed fixture window'
    if ($childWindow.Current.ControlType -ne [System.Windows.Automation.ControlType]::Window -or $childWindow.Current.NativeWindowHandle -eq 0 -or $windowTimer.Elapsed.TotalMilliseconds -lt 700) { throw 'Window discovery accepted the duplicate title TextBlock instead of waiting for its window.' }
    $childWindow.GetCurrentPattern([System.Windows.Automation.WindowPattern]::Pattern).Close()
    Write-Output 'PASS window discovery ignores duplicate title text and waits for the actual owned window'
    $viewport = Find-Element $window 'Fixture.Scroll' -ById
    $scroll = $viewport.GetCurrentPattern([System.Windows.Automation.ScrollPattern]::Pattern)
    if (!$scroll.Current.VerticallyScrollable) { throw 'The short fixture must have overflowing content.' }
    if (!(Find-ScrolledElement $window 'Scroll witness' 'Fixture.Scroll')) { throw 'Offscreen content was not made reachable.' }
    if ($scroll.Current.VerticalScrollPercent -le 0) { throw 'Visible-content lookup did not scroll the short viewport.' }
    if (Find-ScrolledElement $window 'NeverCreated' 'Fixture.Scroll') { throw 'Scrolling invented a missing element.' }
    Write-Output 'PASS short hidden-scrollbar viewport reveals offscreen controls and rejects absent content across all pages'
    $window.GetCurrentPattern([System.Windows.Automation.WindowPattern]::Pattern).Close()
    if (!$owned.WaitForExit(5000)) { throw 'Readiness fixture did not exit.' }
} catch {
    if (Test-Path -LiteralPath (Join-Path $fixture 'window-phase')) { Write-Output ('Fixture phase: ' + [IO.File]::ReadAllText((Join-Path $fixture 'window-phase'))) }
    if ($owned) {
        $processCondition = New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::ProcessIdProperty, $owned.Id)
        $windows = [System.Windows.Automation.AutomationElement]::RootElement.FindAll([System.Windows.Automation.TreeScope]::Children, $processCondition)
        foreach ($item in $windows) {
            Write-Output ('Fixture window: ' + $item.Current.Name + '; type=' + $item.Current.ControlType.ProgrammaticName + '; HWND=' + $item.Current.NativeWindowHandle)
            $children = $item.FindAll([System.Windows.Automation.TreeScope]::Descendants, [System.Windows.Automation.Condition]::TrueCondition)
            foreach ($child in @($children | Select-Object -First 30)) { Write-Output ('Fixture child: ' + $child.Current.Name + '; type=' + $child.Current.ControlType.ProgrammaticName + '; HWND=' + $child.Current.NativeWindowHandle) }
        }
    }
    throw
} finally {
    if ($owned) { $owned.Refresh(); if (!$owned.HasExited) { $owned.Kill(); if (!$owned.WaitForExit(5000)) { throw 'Owned fixture did not stop; its files are retained.' } }; $owned.Dispose() }
    if ([IO.Path]::GetDirectoryName($fixture) -eq $artifactRoot -and [IO.Path]::GetFileName($fixture) -match '^ui-ready-[a-f0-9]{32}$' -and (Test-Path -LiteralPath $fixture) -and [IO.File]::ReadAllText((Join-Path $fixture '.fixture')) -eq 'CodexMeter.UiReadinessFixture.v1') { Remove-Item -LiteralPath $fixture -Recurse -Force }
}
