using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using CodexMeter.Core;

namespace CodexMeter.Tests
{
    internal static partial class Program
    {
        private static void FinalParityFeatures()
        {
            var reserve = new QuotaWindow { Id = "codex_gpt5_reserve:10080", Bucket = "codex_gpt5_reserve", DurationMinutes = 10080 };
            var presentation = new Settings(); Check(!presentation.IsWindowVisible(reserve), "unused Reserve weekly quota starts hidden as in upstream defaults");
            presentation.WindowOrder = new[] { reserve.Id }; Check(presentation.IsWindowVisible(reserve), "explicit saved visibility can show the Reserve weekly quota");
            presentation.HiddenWindows = new[] { reserve.Id }; Check(!presentation.IsWindowVisible(reserve), "explicit Reserve hiding remains authoritative after ordering");
            var channel = new RemoteChannel { Kind = "Bark", Endpoint = "https://example.test", Secret = "fixture", IconUrl = "https://example.test/icon.png", FiveHour = false };
            Check(!channel.Includes("codex:300") && channel.Includes("codex:10080") && !channel.Includes("other:10080"), "remote reminder window switches exclude disabled and unrelated buckets");
            using (var request = RemoteDelivery.Request(channel, "test", "test")) Check(Json.Text(Json.Parse(request.Content.ReadAsStringAsync().Result), "icon") == channel.IconUrl, "Bark includes an explicitly configured HTTPS icon");
            channel.Kind = "Gotify"; channel.Priority = 8;
            using (var request = RemoteDelivery.Request(channel, "test", "test")) Check(Json.Number(Json.Parse(request.Content.ReadAsStringAsync().Result), "priority") == 8, "Gotify uses the configured message priority");
            channel.IconUrl = "http://example.test/icon"; Reject(() => RemoteDelivery.Validate(channel), "remote icon rejects insecure URLs"); channel.IconUrl = "";
            channel.Priority = 11; Reject(() => RemoteDelivery.Validate(channel), "Gotify priority is bounded");
            var profile = new RemoteProfile(); profile.Channels[0].FiveHour = false; profile.Channels[0].IconUrl = "https://example.test/icon"; profile.Channels[1].Priority = 8;
            var vault = new MemorySecretVault(); profile.Save(vault); var restored = RemoteProfile.Load(vault);
            Check(!restored.Channels[0].FiveHour && restored.Channels[0].Weekly && restored.Channels[0].IconUrl.EndsWith("icon") && restored.Channels[1].Priority == 8, "encrypted profile format retains window choices, icon and priority");
            var now = DateTimeOffset.UtcNow; var large = new List<QuotaSample>();
            for (int i = 0; i < 200000; i++) large.Add(new QuotaSample { At = now.AddSeconds(i), WindowId = "codex:10080", Remaining = 100 - i / 2000.0, DurationMinutes = 10080, ResetsAt = now.AddDays(7) });
            var watch = Stopwatch.StartNew(); var reduced = HistorySeries.DisplayPoints(large, now, now.AddSeconds(200000), 800); watch.Stop();
            Check(reduced.Count <= 1600 && reduced.First() == large.First() && reduced.Last() == large.Last(), "display reduction bounds two hundred thousand samples while preserving endpoints");
            Check(large.Count == 200000 && large[12345].Remaining == 100 - 12345 / 2000.0, "display reduction never changes persisted or analytic observations");
            Check(HistorySeries.Nearest(large, now.AddSeconds(12345.2)) == large[12345] && HistorySeries.Nearest(large, now.AddDays(-1)) == large[0] && HistorySeries.Nearest(large, now.AddDays(9)) == large.Last(), "binary hover selection retains exact samples and handles outside endpoints");
            Check(HistorySeries.Nearest(new List<QuotaSample>(), now) == null, "empty chart hover remains unavailable");
            var gaps = new List<QuotaSample> { large[0], large[1], large[5000], large[5001] };
            Check(HistorySeries.DisplayPoints(gaps, now, now.AddDays(7), 1).Count == 4, "display reduction preserves both sides of an unobserved gap");
            Console.WriteLine("PERF display reduction: 200000 observations -> " + reduced.Count + " points, " + watch.ElapsedMilliseconds + " ms (measurement only)");
            string directory = TestDirectory("long-history");
            try
            {
                using (var history = new HistoryStore(Path.Combine(directory, "history.sqlite3")))
                {
                    var window = new List<QuotaWindow> { new QuotaWindow { Id = "codex:300", UsedPercent = 30 } };
                    var usage = new TokenUsage { DailyAvailable = true, Daily = new List<DailyTokens> { new DailyTokens { Date = now.UtcDateTime.ToString("yyyy-MM-dd"), Tokens = 100 } } };
                    history.Record("account", now, window, usage); long revision = history.Revision;
                    history.Record("account", now.AddMinutes(1), window, usage);
                    Check(history.Revision == revision, "unchanged quota and daily Token responses produce no database row rewrites");
                    usage.Daily[0].Tokens = 101; history.Record("account", now.AddMinutes(2), window, usage);
                    Check(history.Revision > revision && history.ReadTokens("account", "0001-01-01", "9999-12-31").Single().Tokens == 101, "changed Token totals still update the stored value");
                    watch.Restart();
                    for (int i = 1; i <= 10000; i++) { window[0].UsedPercent = i % 100; history.Record("account", now.AddMinutes(i), window, null); }
                    watch.Stop(); long writeMilliseconds = watch.ElapsedMilliseconds; watch.Restart();
                    var rows = history.ReadQuota("account", now, now.AddDays(10), "codex:300"); watch.Stop();
                    Check(rows.Count == 10001 && rows.Select(r => r.WindowId).Distinct().Single() == "codex:300", "indexed quota range query preserves all ten thousand observations in the chosen partition");
                    Console.WriteLine("PERF SQLite: " + rows.Count + " rows, writes=" + writeMilliseconds + " ms, range read=" + watch.ElapsedMilliseconds + " ms (measurement only)");
                }
            }
            finally { Directory.Delete(directory, true); }
        }
        private static void InterruptRestore(string directory)
        {
            using (var history = new HistoryStore(Path.Combine(directory, "target.sqlite3")))
            using (var prepared = HistoryBackup.Prepare(Path.Combine(directory, "incoming.codexbackup"), directory))
                history.RestoreSnapshot(prepared.DatabasePath, Path.Combine(directory, "before-crash.codexbackup"), () => Environment.Exit(73));
        }
        private static async Task InterruptedRestoreFeatures()
        {
            string directory = TestDirectory("interrupted-restore"); var now = DateTimeOffset.UtcNow; string oldKey;
            try
            {
                var windows = new List<QuotaWindow> { new QuotaWindow { Id = "codex:300", UsedPercent = 40 } };
                string target = Path.Combine(directory, "target.sqlite3");
                using (var original = new HistoryStore(target)) { oldKey = original.AccountKey("chatgpt", "old@example.test", "a"); original.Record(oldKey, now, windows, null); }
                using (var incoming = new HistoryStore(Path.Combine(directory, "source.sqlite3"))) { incoming.Record("replacement", now, windows, null); HistoryBackup.Create(incoming, Path.Combine(directory, "incoming.codexbackup")); }
                string executable = System.Reflection.Assembly.GetExecutingAssembly().Location;
                string arguments = "--interrupt-restore " + LaunchCommand.Quote(directory);
                if (!executable.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
                {
                    string host = Process.GetCurrentProcess().MainModule.FileName;
                    if (Path.GetFileNameWithoutExtension(host).Equals("dotnet", StringComparison.OrdinalIgnoreCase)) arguments = LaunchCommand.Quote(executable) + " " + arguments;
                    executable = host;
                }
                using (var child = Process.Start(new ProcessStartInfo(executable, arguments) { UseShellExecute = false, CreateNoWindow = true }))
                { await Task.Run(() => { if (!child.WaitForExit(15000)) { child.Kill(); throw new Exception("Restore fixture did not exit."); } }); Check(child.ExitCode == 73 && File.Exists(target + ".restore-journal.json"), "real child terminates after database replacement with a durable recovery journal"); }
                using (var recovered = new HistoryStore(target))
                    Check(recovered.Accounts().Single().Key == oldKey && recovered.AccountKey("chatgpt", "old@example.test", "b") == oldKey && !File.Exists(target + ".restore-journal.json"), "next startup recovers the old rows and identity salt after an interrupted restore");
                using (var backup = HistoryBackup.Prepare(Path.Combine(directory, "before-crash.codexbackup"), directory)) Check(backup.Info.QuotaRows == 1, "interrupted restore retains a separately readable pre-restore backup");
                string candidate = target + ".restore-" + Guid.NewGuid().ToString("N"), rawRollback = target + ".rollback-" + Guid.NewGuid().ToString("N");
                File.Copy(target, candidate); HistoryRestoreJournal.Prepare(target, rawRollback, candidate);
                using (var recovered = new HistoryStore(target)) Check(recovered.Accounts().Single().Key == oldKey && !File.Exists(candidate), "restart before atomic replacement keeps the original database and removes the owned candidate");
                string oldHash = PortableFiles.Sha256(target);
                File.WriteAllText(target + ".restore-journal.json", "{invalid"); bool mapped = false;
                try { using (var broken = new HistoryStore(target)) { } } catch (IOException) { mapped = true; }
                Check(mapped && PortableFiles.Sha256(target) == oldHash, "corrupt recovery metadata becomes a safe history error without modifying records");
                File.Delete(target + ".restore-journal.json");
            }
            finally { Directory.Delete(directory, true); }
        }
    }
}
