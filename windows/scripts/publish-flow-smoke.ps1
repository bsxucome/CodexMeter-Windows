param()
$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path (Split-Path $PSScriptRoot -Parent) -Parent
$fixtureRoot = [IO.Path]::GetFullPath((Join-Path $projectRoot 'windows/artifacts/test-data'))
$fixture = [IO.Path]::GetFullPath((Join-Path $fixtureRoot ('publish flow-' + [Guid]::NewGuid().ToString('N'))))
$gitPath = (Get-Command git -CommandType Application).Source
$names = @('PATH','GIT_CONFIG_GLOBAL','GIT_CONFIG_NOSYSTEM','GIT_CONFIG_COUNT','GIT_CONFIG_PARAMETERS','GIT_TEST_ASSUME_DIFFERENT_OWNER','CODEXMETER_PUBLISH_FIXTURE','CODEXMETER_PUBLISH_MODE','CODEXMETER_FIXTURE_GIT')
$saved = @{}
foreach ($name in $names) { $saved[$name] = [Environment]::GetEnvironmentVariable($name, 'Process') }
try {
    $bin = Join-Path $fixture 'bin'
    [IO.Directory]::CreateDirectory($bin) | Out-Null
    $mockSource = @'
using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
public static class GitHubFixture {
    public static int Main(string[] args) {
        string root = Environment.GetEnvironmentVariable("CODEXMETER_PUBLISH_FIXTURE");
        if (root == null || !File.Exists(Path.Combine(root, ".fixture")) || File.ReadAllText(Path.Combine(root, ".fixture")) != "CodexMeter.PublishFixture.v1") return 98;
        string mode = Environment.GetEnvironmentVariable("CODEXMETER_PUBLISH_MODE");
        string command = string.Join(" ", args.Take(2));
        File.AppendAllText(Path.Combine(root, "windows/artifacts/calls.log"), command + "\n");
        if (command == "auth status") { Console.Error.WriteLine("You are not logged into any GitHub hosts."); return 1; }
        if (command == "auth login") {
            if (!args.Contains("--web")) return 97;
            Console.Error.WriteLine("Fixture device authorization prompt; no real login.");
            return mode == "login-failure" ? 1 : 0;
        }
        if (command == "api user") { Console.WriteLine("{\"login\":\"fixture-user\",\"id\":1}"); return 0; }
        if (command == "repo view") {
            if (mode == "existing-repository" || File.Exists(Path.Combine(root, "windows/artifacts/created"))) {
                Console.WriteLine(args.Contains("url") ? "https://github.com/fixture-user/Fixture" : "{\"nameWithOwner\":\"fixture-user/Fixture\"}"); return 0;
            }
            Console.Error.WriteLine("Fixture repository does not exist."); return 1;
        }
        if (command == "repo create") {
            int description = Array.IndexOf(args, "--description");
            if (!args.Contains("--public") || args[2] != "fixture-user/Fixture" || description < 0 || description + 2 != args.Length || !args[description + 1].Contains("optional notifications.")) return 96;
            var info = new ProcessStartInfo(Environment.GetEnvironmentVariable("CODEXMETER_FIXTURE_GIT"), "remote add origin \"" + Path.Combine(root, "windows/artifacts/remote.git") + "\"") { UseShellExecute = false, CreateNoWindow = true, WorkingDirectory = root };
            using (var git = Process.Start(info)) { git.WaitForExit(); if (git.ExitCode != 0) return git.ExitCode; }
            File.WriteAllText(Path.Combine(root, "windows/artifacts/created"), "fixture");
            Console.Error.WriteLine("Fixture repository creation progress."); return 0;
        }
        if (command == "auth setup-git") { Console.Error.WriteLine("Fixture credential-helper progress; configuration unchanged."); return 0; }
        return 95;
    }
}
'@
    Add-Type -TypeDefinition $mockSource -OutputAssembly (Join-Path $bin 'gh.exe') -OutputType ConsoleApplication
    [Environment]::SetEnvironmentVariable('PATH', $bin + ';' + $saved['PATH'], 'Process')
    [Environment]::SetEnvironmentVariable('GIT_CONFIG_NOSYSTEM', '1', 'Process')
    [Environment]::SetEnvironmentVariable('GIT_CONFIG_COUNT', '0', 'Process')
    [Environment]::SetEnvironmentVariable('GIT_CONFIG_PARAMETERS', $null, 'Process')
    [Environment]::SetEnvironmentVariable('GIT_TEST_ASSUME_DIFFERENT_OWNER', $null, 'Process')
    [Environment]::SetEnvironmentVariable('CODEXMETER_FIXTURE_GIT', $gitPath, 'Process')
    foreach ($scenario in @('success','login-failure','existing-repository')) {
        $root = Join-Path $fixture $scenario
        foreach ($directory in @('.github','windows/scripts','windows/artifacts')) { [IO.Directory]::CreateDirectory((Join-Path $root $directory)) | Out-Null }
        foreach ($file in @('.gitattributes','AGENTS.md','CONTRIBUTING.md','LICENSE','README.md','UPSTREAM.md','.github/fixture.txt')) { [IO.File]::WriteAllText((Join-Path $root $file), "Publish fixture`n") }
        [IO.File]::WriteAllText((Join-Path $root '.gitignore'), "windows/artifacts/`n")
        [IO.File]::WriteAllText((Join-Path $root '.fixture'), 'CodexMeter.PublishFixture.v1')
        Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'publish-github.ps1') -Destination (Join-Path $root 'windows/scripts/publish-github.ps1')
        $globalFile = Join-Path $root 'windows/artifacts/global.gitconfig'
        [IO.File]::WriteAllText($globalFile, '')
        [Environment]::SetEnvironmentVariable('GIT_CONFIG_GLOBAL', $globalFile, 'Process')
        [Environment]::SetEnvironmentVariable('CODEXMETER_PUBLISH_FIXTURE', $root, 'Process')
        [Environment]::SetEnvironmentVariable('CODEXMETER_PUBLISH_MODE', $scenario, 'Process')
        $remote = Join-Path $root 'windows/artifacts/remote.git'
        & $gitPath init --bare --initial-branch=main $remote | Out-Null
        if ($LASTEXITCODE -ne 0) { throw 'Bare fixture initialization failed.' }
        $failure = $null
        try { & (Join-Path $root 'windows/scripts/publish-github.ps1') -Owner fixture-user -RepositoryName Fixture -Visibility public | Out-Null }
        catch { $failure = $_.Exception.Message }
        $calls = [IO.File]::ReadAllLines((Join-Path $root 'windows/artifacts/calls.log'))
        if ($calls -notcontains 'auth login') { throw 'Logged-out probe did not reach browser authorization.' }
        if ($scenario -eq 'success') {
            if ($failure) { throw $failure }
            $head = & $gitPath -C $root rev-parse HEAD
            $pushed = & $gitPath --git-dir=$remote rev-parse refs/heads/main
            if ($LASTEXITCODE -ne 0 -or $head -ne $pushed -or $calls -notcontains 'repo create' -or $calls -notcontains 'auth setup-git') { throw 'Fixture did not complete actual local Git push.' }
            Write-Output 'PASS logged-out stderr reaches authorization; missing repository reaches creation; stderr progress permits actual local Git commit/push'
        } else {
            $expected = if ($scenario -eq 'login-failure') { 'GitHub browser authorization did not complete.' } else { 'That repository already exists; choose a new repository name. Nothing was pushed.' }
            if ($failure -ne $expected -or $calls -contains 'repo create' -or (Test-Path -LiteralPath (Join-Path $root 'windows/artifacts/created'))) { throw 'Failure guard did not stop publication.' }
            Write-Output "PASS $scenario preserves the expected failure reason and prevents repository creation/push"
        }
        if ((Get-Item -LiteralPath $globalFile).Length -ne 0) { throw 'Fixture modified global Git configuration.' }
    }
} finally {
    foreach ($name in $names) { [Environment]::SetEnvironmentVariable($name, $saved[$name], 'Process') }
    if ([IO.Path]::GetDirectoryName($fixture) -eq $fixtureRoot -and [IO.Path]::GetFileName($fixture) -match '^publish flow-[a-f0-9]{32}$' -and (Test-Path -LiteralPath $fixture)) { Remove-Item -LiteralPath $fixture -Recurse -Force }
}
