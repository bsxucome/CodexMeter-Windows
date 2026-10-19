param()
$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path (Split-Path $PSScriptRoot -Parent) -Parent
$fixtureRoot = [IO.Path]::GetFullPath((Join-Path $projectRoot 'windows/artifacts/test-data'))
$fixture = [IO.Path]::GetFullPath((Join-Path $fixtureRoot ('release cli-' + [Guid]::NewGuid().ToString('N'))))
$names = @('PATH','CODEXMETER_RELEASE_FIXTURE','CODEXMETER_RELEASE_MODE')
$saved = @{}
foreach ($name in $names) { $saved[$name] = [Environment]::GetEnvironmentVariable($name, 'Process') }
try {
    $bin = Join-Path $fixture 'bin'
    [IO.Directory]::CreateDirectory($bin) | Out-Null
    $mock = @'
using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Web.Script.Serialization;
public static class ReleaseFixtureCli {
    static string root, mode;
    static string sha = new string('a', 40), tag = "windows-v0.7.0";
    static bool Has(string name) { return File.Exists(Path.Combine(root, name)); }
    static void Put(string name) { File.WriteAllText(Path.Combine(root, name), "fixture"); }
    static void Json(object value) { Console.WriteLine(new JavaScriptSerializer().Serialize(value)); }
    static object Run(int id, string branch, string conclusion) { return new { databaseId = id, headSha = sha, headBranch = branch, status = "completed", conclusion = conclusion, url = "https://github.com/bsxucome/CodexMeter-for-Windows/actions/runs/" + id, workflowName = "Windows builds" }; }
    public static int Main(string[] args) {
        root = Environment.GetEnvironmentVariable("CODEXMETER_RELEASE_FIXTURE"); mode = Environment.GetEnvironmentVariable("CODEXMETER_RELEASE_MODE");
        if (root == null || !Has(".fixture") || File.ReadAllText(Path.Combine(root, ".fixture")) != "CodexMeter.ReleaseFixture.v1") return 99;
        bool git = Path.GetFileNameWithoutExtension(Assembly.GetExecutingAssembly().Location) == "git";
        if (git) {
            if (args.Length < 5 || args[0] != "-c" || args[2] != "-C" || args[3] != root) return 98;
            args = args.Skip(4).ToArray();
        }
        File.AppendAllText(Path.Combine(root, "calls.log"), (git ? "git " : "gh ") + string.Join(" ", args) + "\n");
        string command = string.Join(" ", args.Take(2));
        if (git) {
            if (command == "remote get-url") { Console.WriteLine(mode == "wrong-origin" ? "https://github.com/another/repo.git" : "https://github.com/bsxucome/CodexMeter-for-Windows.git"); return 0; }
            if (command == "branch --show-current") { Console.WriteLine("main"); return 0; }
            if (command == "status --porcelain") { if (mode == "dirty") Console.WriteLine(" M tracked.cs"); return 0; }
            if (command == "rev-parse HEAD") { Console.WriteLine(sha); return 0; }
            if (command == "tag --list") { if (mode == "local-conflict") Console.WriteLine(tag); return 0; }
            if (args[0] == "rev-parse") { Console.WriteLine(mode == "local-conflict" ? new string('b', 40) : sha); return 0; }
            if (command == "ls-remote --tags") { if (mode == "remote-conflict" || mode == "existing") Console.WriteLine((mode == "existing" ? sha : new string('b', 40)) + "\trefs/tags/" + tag); return 0; }
            if (command == "push origin") {
                Console.Error.WriteLine("Fixture native Git progress; no network.");
                if (args[2] == "main") Put("main-pushed");
                else { if (!Has("tag-created")) return 97; Put("tag-pushed"); }
                return 0;
            }
            if (args[0] == "tag") { if (!Has("main-green")) return 96; Put("tag-created"); return 0; }
            return 95;
        }
        if (command == "auth status") { Console.Error.WriteLine("Fixture authentication status."); return mode == "auth-failed" ? 1 : 0; }
        if (command == "run list") {
            string branch = args[Array.IndexOf(args, "--branch") + 1];
            if (branch == "main") { Put("main-green"); Json(new[] { Run(101, "main", mode == "main-failed" ? "failure" : "success") }); }
            else { if (!Has("tag-pushed")) return 94; if (mode != "tag-failed") Put("published"); Json(new[] { Run(999, "main", "success"), Run(202, tag, mode == "tag-failed" ? "failure" : "success") }); }
            return 0;
        }
        if (command == "run view") { Console.WriteLine("Fixture failed CI step."); return 0; }
        if (args[0] == "api") {
            if (mode == "release-network") { Console.Error.WriteLine("Fixture network failure: https://user:PRIVATE_SENTINEL@example.test/ " + "gh" + "p_" + new string('x', 30)); return 1; }
            if (mode != "existing" && !Has("published")) { Console.Error.WriteLine("gh: Not Found (HTTP 404)"); return 1; }
            var assets = new[] { "framework-x64.zip", "framework-x64.zip.sha256", "dotnet10-x64.zip", "dotnet10-x64.zip.sha256" }.Where(x => mode != "missing-asset" || x != "dotnet10-x64.zip.sha256").Select(x => new { name = "CodexMeter-Windows-0.7.0-" + x, state = "uploaded", size = 128 }).ToArray();
            Json(new { tag_name = tag, draft = false, html_url = "https://github.com/bsxucome/CodexMeter-for-Windows/releases/tag/" + tag, assets = assets }); return 0;
        }
        if (command == "release create") {
            string notes = args[Array.IndexOf(args, "--notes-file") + 1];
            string[] assets = args.Skip(Array.IndexOf(args, "--notes-file") + 2).ToArray();
            if (args[2] != tag || !args.Contains("--verify-tag") || assets.Length != 4 || assets.Any(x => !File.Exists(x)) || !File.ReadAllText(notes).Contains("PREPARED_RELEASE_NOTES")) return 93;
            Put("release-created"); Console.Error.WriteLine("Fixture publication progress."); return 0;
        }
        return 92;
    }
}
'@
    Add-Type -TypeDefinition $mock -ReferencedAssemblies System.Core.dll,System.Web.Extensions.dll -OutputAssembly (Join-Path $bin 'gh.exe') -OutputType ConsoleApplication
    Copy-Item -LiteralPath (Join-Path $bin 'gh.exe') -Destination (Join-Path $bin 'git.exe')
    [Environment]::SetEnvironmentVariable('PATH', $bin + ';' + $saved['PATH'], 'Process')
    foreach ($scenario in @('success','main-failed','tag-failed','wrong-origin','dirty','auth-failed','local-conflict','remote-conflict','release-network','missing-asset','existing')) {
        $root = Join-Path $fixture $scenario
        foreach ($directory in @('windows/scripts','windows/CodexMeter.Core','windows/releases','windows/artifacts')) { [IO.Directory]::CreateDirectory((Join-Path $root $directory)) | Out-Null }
        [IO.File]::WriteAllText((Join-Path $root '.fixture'), 'CodexMeter.ReleaseFixture.v1')
        [IO.File]::WriteAllText((Join-Path $root 'windows/CodexMeter.Core/ProductVersion.cs'), 'public const string Current = "0.7.0";')
        foreach ($script in @('release-github.ps1','collect-ci-logs.ps1','publish-release.ps1')) { Copy-Item -LiteralPath (Join-Path $PSScriptRoot $script) -Destination (Join-Path $root ('windows/scripts/' + $script)) }
        [Environment]::SetEnvironmentVariable('CODEXMETER_RELEASE_FIXTURE', $root, 'Process')
        [Environment]::SetEnvironmentVariable('CODEXMETER_RELEASE_MODE', $scenario, 'Process')
        $failure = $null
        try { & (Join-Path $root 'windows/scripts/release-github.ps1') | Out-Null } catch { $failure = $_.Exception.Message }
        $result = ConvertFrom-Json -InputObject ([IO.File]::ReadAllText((Join-Path $root 'windows/artifacts/release-logs/latest-result.json')))
        $published = $scenario -in @('success','existing')
        if ($published) {
            if ($failure -or $result.status -ne 'published' -or $result.commit -ne ('a' * 40) -or $result.assets.Count -ne 4) { throw ("Unexpected release failure in $scenario : " + $failure) }
            if ($scenario -eq 'success') {
                $ci = ConvertFrom-Json -InputObject ([IO.File]::ReadAllText((Join-Path $root 'windows/artifacts/ci-logs/latest-result.json')))
                if ($ci.runId -ne '202' -or $ci.reference -ne 'windows-v0.7.0') { throw 'An unrelated main run was mistaken for tag success.' }
            }
        } elseif (!$failure -or $result.status -ne 'failed') { throw ('Release guard did not stop ' + $scenario) }
        if ($scenario -eq 'release-network' -and ($result.error.Contains('PRIVATE_SENTINEL') -or $result.error -match 'ghp_[x]{30}')) { throw 'Release diagnostics exposed credential-like fixture content.' }
        if ($scenario -notin @('success','tag-failed','missing-asset') -and (Test-Path -LiteralPath (Join-Path $root 'tag-pushed'))) { throw ('Unexpected tag push in ' + $scenario) }
        if ($scenario -eq 'main-failed' -and (Test-Path -LiteralPath (Join-Path $root 'tag-created'))) { throw 'Failed main CI still created a tag.' }
        if ($scenario -eq 'existing' -and (Test-Path -LiteralPath (Join-Path $root 'main-pushed'))) { throw 'Existing release was republished.' }
        Write-Output ('PASS release orchestration: ' + $scenario)
    }
    # The real CI publisher validates both hashes before invoking any release command.
    $root = Join-Path $fixture 'success'
    [Environment]::SetEnvironmentVariable('CODEXMETER_RELEASE_FIXTURE', $root, 'Process')
    [Environment]::SetEnvironmentVariable('CODEXMETER_RELEASE_MODE', 'publisher', 'Process')
    [IO.File]::WriteAllText((Join-Path $root 'windows/releases/0.7.0.md'), 'PREPARED_RELEASE_NOTES')
    $assets = Join-Path $root 'windows/artifacts/packages'
    [IO.Directory]::CreateDirectory($assets) | Out-Null
    foreach ($runtime in @('framework-x64','dotnet10-x64')) {
        $archive = Join-Path $assets ("CodexMeter-Windows-0.7.0-$runtime.zip")
        [IO.File]::WriteAllText($archive, 'fixture package bytes')
        [IO.File]::WriteAllText($archive + '.sha256', (Get-FileHash -LiteralPath $archive -Algorithm SHA256).Hash.ToLowerInvariant() + '  ' + [IO.Path]::GetFileName($archive))
    }
    & (Join-Path $root 'windows/scripts/publish-release.ps1') -Repository bsxucome/CodexMeter-for-Windows -Tag windows-v0.7.0 -Directory $assets | Out-Null
    if (!(Test-Path -LiteralPath (Join-Path $root 'release-created'))) { throw 'Release publisher did not reach the fixture CLI.' }
    Remove-Item -LiteralPath (Join-Path $root 'release-created')
    [IO.File]::AppendAllText((Join-Path $assets 'CodexMeter-Windows-0.7.0-dotnet10-x64.zip'), 'tampered')
    $refused = $false
    try { & (Join-Path $root 'windows/scripts/publish-release.ps1') -Repository bsxucome/CodexMeter-for-Windows -Tag windows-v0.7.0 -Directory $assets | Out-Null } catch { if ($_.Exception.Message -notlike '*checksum mismatch*') { throw }; $refused = $true }
    if (!$refused -or (Test-Path -LiteralPath (Join-Path $root 'release-created'))) { throw 'Tampered package reached publication.' }
    Write-Output 'PASS publisher passes prepared notes and four verified assets; tampering stops before publication'
} finally {
    foreach ($name in $names) { [Environment]::SetEnvironmentVariable($name, $saved[$name], 'Process') }
    if ([IO.Path]::GetDirectoryName($fixture) -eq $fixtureRoot -and [IO.Path]::GetFileName($fixture) -match '^release cli-[a-f0-9]{32}$' -and (Test-Path -LiteralPath $fixture)) { Remove-Item -LiteralPath $fixture -Recurse -Force }
}
