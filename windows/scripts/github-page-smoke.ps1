param()
$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path (Split-Path $PSScriptRoot -Parent) -Parent
$fixtureRoot = [IO.Path]::GetFullPath((Join-Path $projectRoot 'windows/artifacts/test-data'))
$fixture = [IO.Path]::GetFullPath((Join-Path $fixtureRoot ('github page-' + [Guid]::NewGuid().ToString('N'))))
$names = @('PATH', 'CODEXMETER_PAGE_FIXTURE', 'CODEXMETER_PAGE_MODE')
$saved = @{}
foreach ($name in $names) { $saved[$name] = [Environment]::GetEnvironmentVariable($name, 'Process') }
try {
    [IO.Directory]::CreateDirectory((Join-Path $fixture 'bin')) | Out-Null
    [IO.File]::WriteAllText((Join-Path $fixture '.fixture'), 'CodexMeter.PageFixture.v1')
    $mock = @'
using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Web.Script.Serialization;
public static class PageFixtureCli {
    public static int Main(string[] args) {
        string root = Environment.GetEnvironmentVariable("CODEXMETER_PAGE_FIXTURE"), mode = Environment.GetEnvironmentVariable("CODEXMETER_PAGE_MODE");
        if (root == null || !File.Exists(Path.Combine(root, ".fixture")) || File.ReadAllText(Path.Combine(root, ".fixture")) != "CodexMeter.PageFixture.v1") return 99;
        Console.OutputEncoding = new UTF8Encoding(false);
        if (Path.GetFileNameWithoutExtension(Assembly.GetExecutingAssembly().Location) == "git") {
            if (args.Length < 5 || args[0] != "-c" || args[2] != "-C" || args[3] != root) return 98;
            string command = string.Join(" ", args.Skip(4));
            bool legacy = mode == "success-legacy" || mode == "success-redirect" || mode == "rename-failed";
            string target = "https://github.com/bsxucome/CodexMeter-for-Windows.git";
            string original = legacy ? "https://github.com/bsxucome/CodexMeter-Windows.git" : target;
            if (command == "remote get-url --push --all origin" || command == "remote get-url --all origin") {
                string slot = command.Contains("--push") ? "origin-push" : "origin-fetch";
                Console.WriteLine(mode == "wrong-origin" ? "https://github.com/other/repo.git" : File.Exists(Path.Combine(root, slot)) ? File.ReadAllText(Path.Combine(root, slot)) : original);
                if (mode == "multiple-origins") Console.WriteLine("https://github.com/other/repo.git");
            }
            else if (command == "branch --show-current") Console.WriteLine(mode == "wrong-branch" ? "feature" : "main");
            else if (command == "status --porcelain --untracked-files=no") { if (mode == "dirty") Console.WriteLine(" M README.md"); }
            else if (command == "rev-parse HEAD") Console.WriteLine(new string('a', 40));
            else if (command == "remote set-url origin " + target || command == "remote set-url --push origin " + target) {
                if (!File.Exists(Path.Combine(root, "patched"))) return 89;
                File.WriteAllText(Path.Combine(root, command.Contains("--push") ? "origin-push" : "origin-fetch"), target);
            }
            else return 97;
            return 0;
        }
        File.AppendAllText(Path.Combine(root, "api-calls"), string.Join(" ", args) + "\n");
        if (args.Length < 2 || args[0] != "api") return 96;
        string endpoint = args.FirstOrDefault(x => x.StartsWith("repos/"));
        var json = new JavaScriptSerializer();
        if (endpoint == "repos/bsxucome/CodexMeter-for-Windows/git/ref/heads/main") {
            if (args.Contains("--method") || !File.Exists(Path.Combine(root, "pushed"))) return 84;
            Console.WriteLine(json.Serialize(new { @object = new { sha = new string(mode == "wrong-remote-sha" ? 'b' : 'a', 40) } })); return 0;
        }
        string releasePrefix = "repos/bsxucome/CodexMeter-for-Windows/releases/";
        if (endpoint == releasePrefix + "tags/windows-v0.7.0") {
            if (args.Contains("--method")) return 88;
            if (mode == "release-missing") { Console.Error.WriteLine("gh: Not Found (HTTP 404)"); return 1; }
            var text = File.Exists(Path.Combine(root, "release-text")) ? json.Deserialize<System.Collections.Generic.Dictionary<string, object>>(File.ReadAllText(Path.Combine(root, "release-text"))) : null;
            Console.WriteLine(json.Serialize(new { id = 42, tag_name = "windows-v0.7.0", draft = false, name = text == null ? "old" : text["name"], body = text == null ? "old" : text["body"] })); return 0;
        }
        if (endpoint == releasePrefix + "42") {
            if (!args.Contains("--method") || args[Array.IndexOf(args, "--method") + 1] != "PATCH") return 87;
            if (mode == "release-failed") { Console.Error.WriteLine("Fixture release edit rejected."); return 1; }
            string bodyPath = args[Array.IndexOf(args, "--input") + 1];
            if (!bodyPath.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)) return 86;
            var body = json.Deserialize<System.Collections.Generic.Dictionary<string, object>>(File.ReadAllText(bodyPath));
            if (body.Count != 2 || (string)body["name"] != "CodexMeter for Windows 0.7.0" || !((string)body["body"]).Contains("README.en.md")) return 85;
            File.WriteAllText(Path.Combine(root, "release-text"), File.ReadAllText(bodyPath)); Console.WriteLine("{}"); return 0;
        }
        if (endpoint != "repos/bsxucome/CodexMeter-for-Windows" && endpoint != "repos/bsxucome/CodexMeter-Windows" && endpoint != "repos/bsxucome/CodexMeter-for-Windows/topics") return 95;
        if (args.Contains("--method")) {
            if (mode == "metadata-failed" || mode == "rename-failed") { Console.Error.WriteLine("fixture API failure https://user:PRIVATE_SENTINEL@example.test/ " + "gh" + "p_" + new string('x', 30)); return 1; }
            string method = args[Array.IndexOf(args, "--method") + 1], bodyPath = args[Array.IndexOf(args, "--input") + 1];
            if (!bodyPath.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)) return 93;
            byte[] bytes = File.ReadAllBytes(bodyPath);
            if (bytes.Length > 2 && bytes[0] == 239 && bytes[1] == 187 && bytes[2] == 191) return 92;
            if (method == "PATCH" && !endpoint.EndsWith("/topics")) {
                var body = json.Deserialize<System.Collections.Generic.Dictionary<string, object>>(File.ReadAllText(bodyPath));
                bool rename = endpoint.EndsWith("/CodexMeter-Windows");
                if (body.Count != (rename ? 3 : 2) || !body.ContainsKey("description") || !body.ContainsKey("homepage") || (rename && (!body.ContainsKey("name") || (string)body["name"] != "CodexMeter-for-Windows"))) return 91;
                File.WriteAllText(Path.Combine(root, "patched"), File.ReadAllText(bodyPath));
            } else if (method == "PUT" && endpoint.EndsWith("/topics")) {
                if (!File.Exists(Path.Combine(root, "pushed"))) return 94;
                File.WriteAllText(Path.Combine(root, "topics"), File.ReadAllText(bodyPath));
            }
            else return 90;
            Console.WriteLine("{}"); return 0;
        }
        var profile = json.Deserialize<System.Collections.Generic.Dictionary<string, object>>(File.ReadAllText(Path.Combine(root, ".github/repository-profile.json")));
        bool changed = File.Exists(Path.Combine(root, "patched")), topicsChanged = File.Exists(Path.Combine(root, "topics"));
        object topics = topicsChanged ? json.Deserialize<System.Collections.Generic.Dictionary<string, object>>(File.ReadAllText(Path.Combine(root, "topics")))["names"] : new[] { "existing-topic" };
        string canonical = !changed && (mode == "success-legacy" || mode == "rename-failed") ? "bsxucome/CodexMeter-Windows" : "bsxucome/CodexMeter-for-Windows";
        Console.WriteLine(json.Serialize(new { full_name = canonical, description = changed && !(mode == "verify-failed" && topicsChanged) ? profile["description"] : "old", homepage = changed ? profile["homepage"] : "", topics = topics, permissions = new { admin = mode != "no-permission" } }));
        return 0;
    }
}
'@
    $bin = Join-Path $fixture 'bin'
    Add-Type -TypeDefinition $mock -ReferencedAssemblies System.Core.dll,System.Web.Extensions.dll -OutputAssembly (Join-Path $bin 'gh.exe') -OutputType ConsoleApplication
    Copy-Item -LiteralPath (Join-Path $bin 'gh.exe') -Destination (Join-Path $bin 'git.exe')
    [Environment]::SetEnvironmentVariable('PATH', $bin + ';' + $saved['PATH'], 'Process')
    foreach ($scenario in @('success', 'success-legacy', 'success-redirect', 'rename-failed', 'wrong-origin', 'multiple-origins', 'wrong-branch', 'dirty', 'no-permission', 'push-failed', 'wrong-remote-sha', 'metadata-failed', 'verify-failed', 'release-missing', 'release-failed')) {
        $root = Join-Path $fixture $scenario
        foreach ($directory in @('.github', 'windows/scripts', 'windows/releases')) { [IO.Directory]::CreateDirectory((Join-Path $root $directory)) | Out-Null }
        [IO.File]::WriteAllText((Join-Path $root '.fixture'), 'CodexMeter.PageFixture.v1')
        Copy-Item -LiteralPath (Join-Path $projectRoot '.github/repository-profile.json') -Destination (Join-Path $root '.github/repository-profile.json')
        Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'update-github-page.ps1') -Destination (Join-Path $root 'windows/scripts/update-github-page.ps1')
        Copy-Item -LiteralPath (Join-Path $projectRoot 'windows/releases/0.7.0.md') -Destination (Join-Path $root 'windows/releases/0.7.0.md')
        $pushStub = @'
param([switch]$SkipWait)
$root = Split-Path (Split-Path $PSScriptRoot -Parent) -Parent
if (!$SkipWait -or [IO.File]::ReadAllText((Join-Path $root '.fixture')) -ne 'CodexMeter.PageFixture.v1') { throw 'Invalid fixture push.' }
if ($env:CODEXMETER_PAGE_MODE -eq 'push-failed') { throw 'Fixture push failed.' }
[IO.File]::WriteAllText((Join-Path $root 'pushed'), 'fixture')
'@
        [IO.File]::WriteAllText((Join-Path $root 'windows/scripts/sync-ci-fix.ps1'), $pushStub)
        [Environment]::SetEnvironmentVariable('CODEXMETER_PAGE_FIXTURE', $root, 'Process')
        [Environment]::SetEnvironmentVariable('CODEXMETER_PAGE_MODE', $scenario, 'Process')
        $failure = $null
        try { & (Join-Path $root 'windows/scripts/update-github-page.ps1') | Out-Null } catch { $failure = $_.Exception.Message }
        $success = $scenario -like 'success*' -or $scenario -eq 'release-missing'
        if ($success -eq [bool]$failure) { throw ('Incorrect page result: ' + $scenario + ' ' + $failure) }
        if ($success) {
            $result = ConvertFrom-Json -InputObject ([IO.File]::ReadAllText((Join-Path $root 'windows/artifacts/github-page/latest-result.json')))
            if ($result.status -ne 'published' -or $result.commit -ne ('a' * 40) -or 'existing-topic' -notin $result.topics -or $result.description -ne (Get-Content -LiteralPath (Join-Path $root '.github/repository-profile.json') -Raw -Encoding UTF8 | ConvertFrom-Json).description) { throw 'Unicode metadata, source identity or existing topics were lost.' }
            if ($scenario -in @('success-legacy', 'success-redirect')) {
                foreach ($slot in @('origin-fetch', 'origin-push')) { if ([IO.File]::ReadAllText((Join-Path $root $slot)) -ne 'https://github.com/bsxucome/CodexMeter-for-Windows.git') { throw 'Legacy origin did not migrate after confirmed rename.' } }
            }
            if ($scenario -ne 'release-missing' -and !(Test-Path -LiteralPath (Join-Path $root 'release-text'))) { throw 'Existing release text was not localized.' }
        } elseif ($scenario -notin @('push-failed', 'wrong-remote-sha', 'verify-failed', 'release-failed') -and (Test-Path -LiteralPath (Join-Path $root 'patched'))) { throw ('Unexpected repository write: ' + $scenario) }
        if ($scenario -eq 'rename-failed' -and ((Test-Path -LiteralPath (Join-Path $root 'origin-fetch')) -or (Test-Path -LiteralPath (Join-Path $root 'origin-push')) -or (Test-Path -LiteralPath (Join-Path $root 'pushed')))) { throw 'Failed rename changed local remotes or pushed source.' }
        if ($scenario -eq 'metadata-failed' -and ($failure.Contains('PRIVATE_SENTINEL') -or $failure -match 'ghp_[x]{30}')) { throw 'Page diagnostics exposed fixture credentials.' }
        Write-Output ('PASS GitHub page publication: ' + $scenario)
    }
} finally {
    foreach ($name in $names) { [Environment]::SetEnvironmentVariable($name, $saved[$name], 'Process') }
    if ([IO.Path]::GetDirectoryName($fixture) -eq $fixtureRoot -and [IO.Path]::GetFileName($fixture) -match '^github page-[a-f0-9]{32}$' -and (Test-Path -LiteralPath $fixture) -and [IO.File]::ReadAllText((Join-Path $fixture '.fixture')) -eq 'CodexMeter.PageFixture.v1') { Remove-Item -LiteralPath $fixture -Recurse -Force }
}
