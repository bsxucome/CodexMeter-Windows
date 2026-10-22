param(
    [string]$AppPath = (Join-Path (Split-Path $PSScriptRoot -Parent) 'artifacts/CodexMeter-framework-x64/CodexMeter.exe'),
    [string]$OutputPath = (Join-Path (Split-Path (Split-Path $PSScriptRoot -Parent) -Parent) 'docs/images/taskbar-light.png')
)
$ErrorActionPreference = 'Stop'
$app = (Resolve-Path -LiteralPath $AppPath).Path
$core = Join-Path (Split-Path $app -Parent) 'CodexMeter.Core.dll'
if (!(Test-Path -LiteralPath $core)) { throw 'Use the unbundled Framework build to render the shared indicator control.' }
$scratch = Join-Path (Split-Path $PSScriptRoot -Parent) ('artifacts/taskbar-art-' + [Guid]::NewGuid().ToString('N'))
[IO.Directory]::CreateDirectory($scratch) | Out-Null
Copy-Item -LiteralPath $core -Destination $scratch
$framework = Join-Path $env:WINDIR 'Microsoft.NET/Framework64/v4.0.30319'
$wpf = Join-Path $framework 'WPF'
$helper = Join-Path $scratch 'TaskbarPreview.exe'
& (Join-Path $framework 'csc.exe') /nologo /codepage:65001 /langversion:5 /warnaserror+ /platform:x64 /target:exe "/out:$helper" "/r:$core" /r:System.Xaml.dll "/r:$wpf/PresentationCore.dll" "/r:$wpf/PresentationFramework.dll" "/r:$wpf/WindowsBase.dll" "/r:$wpf/UIAutomationTypes.dll" (Join-Path $PSScriptRoot 'TaskbarPreview.cs')
if ($LASTEXITCODE -ne 0) { throw 'Taskbar artwork helper compilation failed.' }
& $helper $app ([IO.Path]::GetFullPath($OutputPath))
if ($LASTEXITCODE -ne 0) { throw 'Taskbar artwork capture failed.' }
