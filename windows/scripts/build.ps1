param(
    [ValidateSet("Auto", "DotNet", "Framework")][string]$Runtime = "Auto",
    [switch]$Publish,
    [switch]$SkipTests,
    [string]$SigningCertificatePath,
    [string]$TimestampServer,
    [string]$ArtifactDirectory
)
$ErrorActionPreference = "Stop"
$windowsRoot = Split-Path $PSScriptRoot -Parent
$projectRoot = Split-Path $windowsRoot -Parent
$artifactRoot = if ($ArtifactDirectory) { [IO.Path]::GetFullPath($ArtifactDirectory) } else { Join-Path $windowsRoot "artifacts" }
$versionSource = [IO.File]::ReadAllText((Join-Path $windowsRoot 'CodexMeter.Core/ProductVersion.cs'))
$releaseVersion = [regex]::Match($versionSource, 'Current = "([^"]+)"').Groups[1].Value
if (!$releaseVersion) { throw 'Missing product version.' }
[xml]$desktopProject = Get-Content -LiteralPath (Join-Path $windowsRoot 'CodexMeter.Windows/CodexMeter.Windows.csproj')
if ($desktopProject.Project.PropertyGroup.Version -ne $releaseVersion) { throw 'SDK and product versions differ.' }
[IO.Directory]::CreateDirectory($artifactRoot) | Out-Null
$localDotnet = Join-Path $projectRoot ".tools/dotnet/dotnet.exe"
$dotnetCommand = if (Test-Path -LiteralPath $localDotnet) { $localDotnet } else { (Get-Command dotnet -ErrorAction SilentlyContinue).Source }
$hasSdk = $false
if ($dotnetCommand) { $hasSdk = @(& $dotnetCommand --list-sdks | Where-Object { $_ -match '^10\.' }).Count -gt 0 }
if ($Runtime -eq "Auto") { $Runtime = if ($hasSdk) { "DotNet" } else { "Framework" } }
if ($Runtime -eq "DotNet") {
    if (!$hasSdk) { throw "Install .NET 10 SDK, or extract it to .tools/dotnet. Existing runtime alone cannot build." }
    & $dotnetCommand build (Join-Path $windowsRoot "CodexMeter.Windows/CodexMeter.Windows.csproj") -c Release
    if ($LASTEXITCODE -ne 0) { throw "Desktop build failed." }
    if (!$SkipTests) {
        & $dotnetCommand run --project (Join-Path $windowsRoot "CodexMeter.Tests/CodexMeter.Tests.csproj") -c Release
        if ($LASTEXITCODE -ne 0) { throw "Tests failed." }
    }
    if ($Publish) {
        $output = Join-Path $artifactRoot ('publish-dotnet-' + [Guid]::NewGuid().ToString('N'))
        & $dotnetCommand publish (Join-Path $windowsRoot "CodexMeter.Windows/CodexMeter.Windows.csproj") -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:PublishTrimmed=false -p:EnableCompressionInSingleFile=false -o $output
        if ($LASTEXITCODE -ne 0) { throw "Publish failed." }
    }
} else {
    $framework = Join-Path $env:WINDIR "Microsoft.NET/Framework64/v4.0.30319"
    $compiler = Join-Path $framework "csc.exe"
    if (!(Test-Path -LiteralPath $compiler)) { throw "Windows .NET Framework compiler not found; use .NET 10 SDK." }
    $output = Join-Path $artifactRoot "CodexMeter-framework-x64"
    [IO.Directory]::CreateDirectory($output) | Out-Null
    $coreFiles = @(Get-ChildItem -LiteralPath (Join-Path $windowsRoot "CodexMeter.Core") -Filter "*.cs" | ForEach-Object FullName)
    $common = @("/nologo", "/codepage:65001", "/langversion:5", "/warnaserror+", "/optimize+", "/define:NETFRAMEWORK", "/platform:x64")
    $coreReferences = @('/r:System.Web.Extensions.dll', '/r:System.Net.Http.dll', '/r:System.IO.Compression.dll', '/r:System.IO.Compression.FileSystem.dll')
    & $compiler @common /target:library "/out:$output/CodexMeter.Core.dll" @coreReferences @coreFiles
    if ($LASTEXITCODE -ne 0) { throw "Core build failed." }
    $desktopFiles = @(Get-ChildItem -LiteralPath (Join-Path $windowsRoot "CodexMeter.Windows") -Filter "*.cs" | ForEach-Object FullName)
    $wpf = Join-Path $framework "WPF"
    & $compiler @common /target:winexe "/out:$output/CodexMeter.exe" "/r:$output/CodexMeter.Core.dll" /r:System.Net.Http.dll /r:System.Drawing.dll /r:System.Windows.Forms.dll /r:System.Xaml.dll "/r:$wpf/PresentationCore.dll" "/r:$wpf/PresentationFramework.dll" "/r:$wpf/WindowsBase.dll" "/r:$wpf/UIAutomationClient.dll" "/r:$wpf/UIAutomationTypes.dll" "/r:$wpf/UIAutomationProvider.dll" "/win32manifest:$windowsRoot/CodexMeter.Windows/app.manifest" @desktopFiles
    if ($LASTEXITCODE -ne 0) { throw "Desktop build failed." }
    Copy-Item -LiteralPath (Join-Path $windowsRoot "CodexMeter.Windows/App.config") -Destination (Join-Path $output "CodexMeter.exe.config") -Force
    if (!$SkipTests) {
        $testFiles = @(Get-ChildItem -LiteralPath (Join-Path $windowsRoot "CodexMeter.Tests") -Filter "*.cs" | ForEach-Object FullName)
        & $compiler @common /target:exe "/out:$output/CodexMeter.Tests.exe" "/r:$output/CodexMeter.Core.dll" @coreReferences @testFiles
        if ($LASTEXITCODE -ne 0) { throw "Test build failed." }
        & (Join-Path $output "CodexMeter.Tests.exe")
        if ($LASTEXITCODE -ne 0) { throw "Tests failed." }
    }
}
if ($output) {
    if ($Runtime -eq 'Framework') {
        # Retain license/documentation access when running an unbundled developer build.
        Copy-Item -LiteralPath (Join-Path $projectRoot 'LICENSE') -Destination $output -Force
        Copy-Item -LiteralPath (Join-Path $projectRoot 'UPSTREAM.md') -Destination $output -Force
        foreach ($name in @('README.md','VERIFICATION.md','PARITY.md','RELEASES.md','ACCEPTANCE.md')) { Copy-Item -LiteralPath (Join-Path $windowsRoot $name) -Destination $output -Force }
    }
    if ($Publish) {
        if ($SigningCertificatePath) {
            if (!$TimestampServer) { throw "A timestamp server is required for Authenticode signing." }
            & (Join-Path $PSScriptRoot 'sign-binaries.ps1') -Directory $output -CertificatePath $SigningCertificatePath -TimestampServer $TimestampServer -Runtime $Runtime
        }
        & (Join-Path $PSScriptRoot 'package-portable.ps1') -Runtime $Runtime -InputDirectory $output -ArtifactDirectory $artifactRoot
    }
    Write-Output "Output: $output"
}
