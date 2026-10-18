using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using CodexMeter.Core;

namespace CodexMeter.Windows
{
    internal static class UpdateWorker
    {
        internal static string Root { get { return UpdateFixture.Enabled ? Path.Combine(UpdateFixture.DirectoryPath, "updates") : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "CodexMeter", "updates"); } }
        private static bool Token(string token) { return Regex.IsMatch(token ?? "", "^[a-f0-9]{32}$"); }
        private static string Job(string token) { if (!Token(token)) throw new InvalidDataException(); return Path.Combine(Root, "jobs", token + ".json"); }
        private static string Receipt(string token) { if (!Token(token)) throw new InvalidDataException(); return Path.Combine(Root, "receipts", token + ".ready"); }
        private static string Executable { get { return Process.GetCurrentProcess().MainModule.FileName; } }
        internal static string Stage(string token) { if (!Token(token)) throw new InvalidDataException(); return Path.Combine(Root, "staging", token); }
        internal static async Task Begin(string stage, SignedRelease release, string key, CancellationToken cancellation)
        {
            if (!Token(stage)) throw new InvalidDataException();
            var current = Process.GetCurrentProcess(); string old = Executable;
            string parent = Path.GetDirectoryName(Path.GetDirectoryName(old));
            if (parent == null) throw new IOException();
            string token = Guid.NewGuid().ToString("N");
            string target = Path.Combine(parent, "CodexMeter-Windows-" + release.Version + "-" + release.Runtime + "-" + token.Substring(0, 8));
            var job = new { stage = stage, old = old, target = target, parentPid = current.Id,
                parentStart = current.StartTime.ToUniversalTime().Ticks.ToString(), channel = release.Channel, runtime = release.Runtime, key = key };
            Directory.CreateDirectory(Path.Combine(Root, "jobs")); Directory.CreateDirectory(Path.Combine(Root, "receipts"));
            PortableFiles.AtomicWrite(Job(token), Encoding.UTF8.GetBytes(Json.Encode(job)));
            string workerArguments = UpdateFixture.Enabled ? "--apply-update-fixture " + token + " " + LaunchCommand.Quote(UpdateFixture.DirectoryPath) : "--apply-update " + token;
            using (var worker = Process.Start(new ProcessStartInfo(old, workerArguments) { UseShellExecute = false, CreateNoWindow = true, WorkingDirectory = Path.GetDirectoryName(old) }))
            {
                bool prepared = false;
                try
                {
                    for (int i = 0; i < 600; i++)
                    {
                        cancellation.ThrowIfCancellationRequested();
                        if (worker.HasExited) throw new IOException("更新器未能准备安装。");
                        string acknowledgement = Path.Combine(Root, "jobs", token + ".prepared");
                        if (File.Exists(acknowledgement) && File.ReadAllText(acknowledgement) == worker.Id.ToString()) { prepared = true; return; }
                        await Task.Delay(100, cancellation);
                    }
                    throw new IOException("更新器准备超时。");
                }
                finally { if (!prepared && !worker.HasExited) { worker.Kill(); worker.WaitForExit(5000); } }
            }
        }
        internal static void Ready(string token)
        {
            if (!Token(token)) return;
            try
            {
                var job = ReadJob(token);
                if (!string.Equals(Executable, Path.Combine(Json.Text(job, "target"), "CodexMeter.exe"), StringComparison.OrdinalIgnoreCase)) return;
                PortableFiles.AtomicWrite(Receipt(token), Encoding.UTF8.GetBytes(Process.GetCurrentProcess().Id.ToString()));
            }
            catch (Exception ex) { if (ex is OutOfMemoryException) throw; }
        }
        private static System.Collections.Generic.Dictionary<string, object> ReadJob(string token)
        { using (var input = File.OpenRead(Job(token))) return Json.Parse(Encoding.UTF8.GetString(PortableFiles.ReadBounded(input, 16384))); }
        internal static int Apply(string token)
        {
            bool exited = false; string old = Executable;
            try
            {
                var job = ReadJob(token);
                if (!string.Equals(Json.Text(job, "old"), old, StringComparison.OrdinalIgnoreCase)) return 2;
                string warning; var settings = UpdateFixture.Enabled ? UpdateFixture.Settings() : SettingsStore.Default().Load(out warning);
                if (settings.UpdatePublicKey != Json.Text(job, "key") || settings.UpdateChannel != Json.Text(job, "channel") ||
                    SignedRelease.CurrentRuntime != Json.Text(job, "runtime")) return 2;
                string stage = Stage(Json.Text(job, "stage"));
                string manifest; using (var input = File.OpenRead(Path.Combine(stage, "manifest.json"))) manifest = Encoding.UTF8.GetString(PortableFiles.ReadBounded(input, 524288));
                var release = SignedRelease.Verify(manifest, settings.UpdatePublicKey, settings.UpdateChannel, SignedRelease.CurrentRuntime);
                if (ReleaseVersion.Parse(release.Version).CompareTo(ReleaseVersion.Parse(AppVersion.Value)) <= 0) return 2;
                string archive = Path.Combine(stage, "package.zip"); release.VerifyArchive(archive);
                UpdateInstall.ValidateDestination(Json.Text(job, "target"), old);
                string probe = Path.Combine(Path.GetDirectoryName(Path.GetDirectoryName(old)), ".codexmeter-write-" + token);
                using (var file = new FileStream(probe, FileMode.CreateNew, FileAccess.Write, FileShare.None, 1, FileOptions.DeleteOnClose)) { }
                int pid = checked((int)Json.Number(job, "parentPid").Value);
                try
                {
                    using (var parent = Process.GetProcessById(pid))
                    {
                        if (!string.Equals(parent.MainModule.FileName, old, StringComparison.OrdinalIgnoreCase) || parent.StartTime.ToUniversalTime().Ticks.ToString() != Json.Text(job, "parentStart")) return 2;
                        PortableFiles.AtomicWrite(Path.Combine(Root, "jobs", token + ".prepared"), Encoding.UTF8.GetBytes(Process.GetCurrentProcess().Id.ToString()));
                        if (!parent.WaitForExit(30000)) return 2;
                    }
                }
                catch (ArgumentException) { }
                exited = true;
                bool installed = UpdateInstall.Run(release, archive, Json.Text(job, "target"), old, next => LaunchAndConfirm(next, token), Restart);
                SaveResult(installed ? "已安装更新；旧版本目录已保留。" : "更新启动失败，已尝试返回旧版本。", release.Version);
                if (installed) MigrateStartup(token);
                return installed ? 0 : 3;
            }
            catch (Exception ex)
            {
                if (ex is OutOfMemoryException) throw;
                if (exited) { try { Restart(old); } catch (Exception) { } }
                SaveResult("更新未完成；旧版本目录已保留。", null); return 1;
            }
        }
        private static bool LaunchAndConfirm(string executable, string token)
        {
            string arguments = UpdateFixture.Enabled ? "--update-token-fixture " + token + " " + LaunchCommand.Quote(UpdateFixture.DirectoryPath) : "--update-token " + token;
            using (var child = Process.Start(new ProcessStartInfo(executable, arguments) { UseShellExecute = false, WorkingDirectory = Path.GetDirectoryName(executable) }))
            {
                bool confirmed = false;
                try
                {
                    for (int i = 0; i < 300; i++)
                    {
                        if (child.HasExited) return false;
                        try { if (File.Exists(Receipt(token)) && File.ReadAllText(Receipt(token)) == child.Id.ToString()) { confirmed = !child.WaitForExit(2000); return confirmed; } } catch (IOException) { }
                        Thread.Sleep(100);
                    }
                    return false;
                }
                finally { if (!confirmed && !child.HasExited) { child.Kill(); child.WaitForExit(5000); } }
            }
        }
        private static void Restart(string executable)
        { Process.Start(new ProcessStartInfo(executable, UpdateFixture.Enabled ? "--rollback-fixture " + LaunchCommand.Quote(UpdateFixture.DirectoryPath) : "") { UseShellExecute = false, WorkingDirectory = Path.GetDirectoryName(executable) }); }
        internal static void MigrateStartup(string token)
        {
            if (UpdateFixture.Enabled) return;
            if (!Token(token)) return;
            try
            {
                var job = ReadJob(token);
                if (!string.Equals(Executable, Json.Text(job, "old"), StringComparison.OrdinalIgnoreCase)) return;
                var registry = new RegistryStartupValueStore();
                var previous = new StartupRegistration(registry, Json.Text(job, "old"));
                if (previous.Read().Registered) new StartupRegistration(registry, Path.Combine(Json.Text(job, "target"), "CodexMeter.exe")).Set(true);
            }
            catch (Exception ex) { if (ex is OutOfMemoryException) throw; SaveResult("更新已启动；登录启动路径未能迁移，请在设置中重新保存。", null); }
        }
        private static void SaveResult(string status, string version)
        {
            try { Directory.CreateDirectory(Root); PortableFiles.AtomicWrite(Path.Combine(Root, "last-result.json"), Encoding.UTF8.GetBytes(Json.Encode(new { status = status, version = version, at = DateTimeOffset.UtcNow.ToString("O") }))); }
            catch (Exception ex) { if (ex is OutOfMemoryException) throw; }
        }
        internal static string LastResult()
        {
            try { using (var input = File.OpenRead(Path.Combine(Root, "last-result.json"))) return Json.Text(Json.Parse(Encoding.UTF8.GetString(PortableFiles.ReadBounded(input, 4096))), "status") ?? ""; }
            catch (Exception ex) { if (ex is OutOfMemoryException) throw; return ""; }
        }
    }
    internal static class AppVersion { internal const string Value = ProductVersion.Current; }
}
