param()
$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path (Split-Path $PSScriptRoot -Parent) -Parent
$fixtureRoot = [IO.Path]::GetFullPath((Join-Path $projectRoot 'windows/artifacts/test-data'))
$fixture = [IO.Path]::GetFullPath((Join-Path $fixtureRoot ('sync cli-' + [Guid]::NewGuid().ToString('N'))))
$names = @('PATH', 'CODEXMETER_SYNC_FIXTURE', 'CODEXMETER_SYNC_MODE')
$saved = @{}
foreach ($name in $names) { $saved[$name] = [Environment]::GetEnvironmentVariable($name, 'Process') }
try {
    $bin = Join-Path $fixture 'bin'
    [IO.Directory]::CreateDirectory($bin) | Out-Null
    [IO.File]::WriteAllText((Join-Path $fixture '.fixture'), 'CodexMeter.SyncFixture.v1')
    $mock = @'
using System;
using System.IO;
using System.Linq;
public static class SyncFixtureCli {
    public static int Main(string[] args) {
        string root = Environment.GetEnvironmentVariable("CODEXMETER_SYNC_FIXTURE");
        string mode = Environment.GetEnvironmentVariable("CODEXMETER_SYNC_MODE");
        if (root == null || !File.Exists(Path.Combine(root, ".fixture")) || File.ReadAllText(Path.Combine(root, ".fixture")) != "CodexMeter.SyncFixture.v1") return 99;
        if (args.Length < 5 || args[0] != "-c" || args[1] != "safe.directory=" + root.Replace('\\', '/') || args[2] != "-C" || args[3] != root) return 98;
        File.AppendAllText(Path.Combine(root, "calls.log"), string.Join("\t", args.Skip(4)) + "\n");
        args = args.Skip(4).ToArray();
        string command = string.Join(" ", args);
        string sha = new string('a', 40);
        if (command == "remote get-url --push origin") { Console.WriteLine(mode == "wrong-origin" ? "https://github.com/other/repo.git" : "https://github.com/bsxucome/CodexMeter-Windows.git"); return 0; }
        if (command == "branch --show-current") { Console.WriteLine(mode == "wrong-branch" ? "feature" : "main"); return 0; }
        if (command == "rev-parse HEAD") { Console.WriteLine(mode == "invalid-sha" ? "invalid" : sha); return 0; }
        int index = Array.IndexOf(args, "push");
        if (index < 0 || args.Length != index + 3 || args[index + 1] != "origin" || args[index + 2] != sha + ":refs/heads/main") return 97;
        if (!args.Contains("http.sslVerify=true") || args.Any(x => x.Contains("false") || x.Contains("force"))) return 96;
        string countPath = Path.Combine(root, "push-count");
        int count = File.Exists(countPath) ? int.Parse(File.ReadAllText(countPath)) + 1 : 1;
        File.WriteAllText(countPath, count.ToString());
        bool fallback = args.Contains("http.sslBackend=openssl") && args.Contains("http.version=HTTP/1.1");
        if ((count == 1 && fallback) || (count == 2 && !fallback) || count > 2) return 95;
        string handshake = "fatal: unable to access 'https://github.com/bsxucome/CodexMeter-Windows.git/': schannel: failed to receive handshake, SSL/TLS connection failed";
        if (mode == "success" || (mode == "handshake-recovered" && count == 2)) { Console.Error.WriteLine("Everything up-to-date (fixture; no network)."); return 0; }
        if (count == 2 && mode == "unsupported-backend") { Console.Error.WriteLine("fatal: Unsupported SSL backend 'openssl'"); return 128; }
        if (mode == "auth") Console.Error.WriteLine("fatal: Authentication failed");
        else if (mode == "connection") Console.Error.WriteLine("fatal: Failed to connect to github.com port 443");
        else {
            // A noisy native stream still leaves the actual error in the bounded tail.
            for (int n = 0; n < 80; n++) Console.WriteLine("Counting fixture objects: " + n);
            Console.Error.WriteLine(handshake);
            if (mode == "certificate") Console.Error.WriteLine("SSL certificate problem: unable to get local issuer certificate");
            if (mode == "rejected") Console.Error.WriteLine("! [rejected] main -> main (non-fast-forward)");
        }
        return 128;
    }
}
'@
    Add-Type -TypeDefinition $mock -Language CSharp -OutputAssembly (Join-Path $bin 'git.exe') -OutputType ConsoleApplication
    [Environment]::SetEnvironmentVariable('PATH', $bin + ';' + $saved['PATH'], 'Process')
    $scenarios = @('success', 'handshake-recovered', 'handshake-failed', 'unsupported-backend', 'auth', 'connection', 'certificate', 'rejected', 'wrong-origin', 'wrong-branch', 'invalid-sha')
    foreach ($scenario in $scenarios) {
        $root = Join-Path $fixture $scenario
        $scripts = Join-Path $root 'windows/scripts'
        [IO.Directory]::CreateDirectory($scripts) | Out-Null
        [IO.File]::WriteAllText((Join-Path $root '.fixture'), 'CodexMeter.SyncFixture.v1')
        Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'sync-ci-fix.ps1') -Destination $scripts
        $collector = @'
param([string]$ExpectedCommit, [switch]$WaitForCompletion)
$root = Split-Path (Split-Path $PSScriptRoot -Parent) -Parent
if ([IO.File]::ReadAllText((Join-Path $root '.fixture')) -ne 'CodexMeter.SyncFixture.v1') { throw 'Missing fixture marker.' }
if ($ExpectedCommit -ne ('a' * 40) -or !$WaitForCompletion) { throw 'Collector must wait for the inspected SHA.' }
[IO.File]::WriteAllText((Join-Path $root 'collected'), $ExpectedCommit)
'@
        [IO.File]::WriteAllText((Join-Path $scripts 'collect-ci-logs.ps1'), $collector)
        [Environment]::SetEnvironmentVariable('CODEXMETER_SYNC_FIXTURE', $root, 'Process')
        [Environment]::SetEnvironmentVariable('CODEXMETER_SYNC_MODE', $scenario, 'Process')
        $failure = $null
        # Suppress the deliberately noisy fixture's console stream, not errors.
        try { & (Join-Path $scripts 'sync-ci-fix.ps1') 6>$null | Out-Null } catch { $failure = $_.Exception.Message }
        $success = $scenario -in @('success', 'handshake-recovered')
        if ($success -eq [bool]$failure -or $success -ne (Test-Path -LiteralPath (Join-Path $root 'collected'))) { throw ('Incorrect sync/CI result: ' + $scenario + ' ' + $failure) }
        $countPath = Join-Path $root 'push-count'
        $count = if (Test-Path -LiteralPath $countPath) { [int][IO.File]::ReadAllText($countPath) } else { 0 }
        $expected = if ($scenario -in @('wrong-origin', 'wrong-branch', 'invalid-sha')) { 0 } elseif ($scenario -in @('handshake-recovered', 'handshake-failed', 'unsupported-backend')) { 2 } else { 1 }
        if ($count -ne $expected) { throw ("Incorrect retry count for ${scenario}: $count, expected $expected") }
        if ($expected -gt 0 -and !$success -and $failure -notlike '*exit code 128*') { throw ('Native stderr bypassed the exit-code check: ' + $failure) }
        $calls = [IO.File]::ReadAllText((Join-Path $root 'calls.log'))
        if ($calls -match '(?im)(--global|--force|\tconfig\t|\tcommit\t|\tadd\t)') { throw 'Sync mutated configuration or staged/committed/forced changes.' }
        Write-Output ('PASS committed-main sync: ' + $scenario)
    }
} finally {
    foreach ($name in $names) { [Environment]::SetEnvironmentVariable($name, $saved[$name], 'Process') }
    if ([IO.Path]::GetDirectoryName($fixture) -eq $fixtureRoot -and [IO.Path]::GetFileName($fixture) -match '^sync cli-[a-f0-9]{32}$' -and (Test-Path -LiteralPath $fixture) -and [IO.File]::ReadAllText((Join-Path $fixture '.fixture')) -eq 'CodexMeter.SyncFixture.v1') { Remove-Item -LiteralPath $fixture -Recurse -Force }
}
