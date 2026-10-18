using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using CodexMeter.Core;

namespace CodexMeter.Tests
{
    internal static partial class Program
    {
        private static void Tokens()
        {
            var usage = TokenUsage.Parse(Json.Parse("{\"summary\":{\"lifetimeTokens\":0,\"peakDailyTokens\":null,\"currentStreakDays\":4},\"dailyUsageBuckets\":null}"));
            Check(usage.Available && usage.LifetimeTokens == 0 && !usage.PeakDailyTokens.HasValue && !usage.DailyAvailable, "token null metrics differ from actual zero");
            Check(!TokenUsage.Parse(Json.Parse("{}")).Available, "absent token API payload is unavailable");
            usage = TokenUsage.Parse(Json.Parse("{\"summary\":{\"lifetimeTokens\":-1,\"peakDailyTokens\":1.2,\"currentStreakDays\":\"2\",\"longestStreakDays\":1e30},\"dailyUsageBuckets\":[{\"startDate\":\"2026-02-30\",\"tokens\":12},{\"startDate\":\"2026-10-18\",\"tokens\":4},{\"startDate\":\"2026-10-18\",\"tokens\":5},{\"startDate\":\"2026-10-17\",\"tokens\":-2}]}"));
            Check(!usage.LifetimeTokens.HasValue && !usage.PeakDailyTokens.HasValue && !usage.CurrentStreakDays.HasValue && !usage.LongestStreakDays.HasValue, "invalid token metrics rejected");
            Check(usage.Daily.Count == 1 && usage.Daily[0].Tokens == 5, "daily tokens validate calendar dates and deduplicate");
            usage = TokenUsage.Parse(Json.Parse("{\"dailyUsageBuckets\":[]}"));
            Check(usage.DailyAvailable && usage.Available && usage.Daily.Count == 0, "empty daily series differs from missing series");
        }
        private static void History()
        {
            var directory = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "test-data", "history 中文 " + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);
            try
            {
                string key; var now = DateTimeOffset.UtcNow;
                var path = Path.Combine(directory, "history.sqlite3");
                var windows = new List<QuotaWindow> { new QuotaWindow { Id = "codex:300", Bucket = "codex", UsedPercent = 20, DurationMinutes = 300, ResetsAt = now.AddHours(4) } };
                using (var store = new HistoryStore(path))
                {
                    key = store.AccountKey("chatgpt", " A@Example.Test ", "one");
                    Check(key == store.AccountKey("chatgpt", "a@example.test", "two") && !key.Contains("example"), "account hash normalizes email without storing it");
                    Check(store.AccountKey("chatgpt", null, "one") != store.AccountKey("chatgpt", null, "two"), "anonymous boundaries partition history");
                    using (var second = new HistoryStore(Path.Combine(directory, "other.sqlite3")))
                        Check(key != second.AccountKey("chatgpt", "a@example.test", "one"), "identity salt differs between installations");
                    store.Record(key, now, windows, null);
                    store.Record(key, now.AddMinutes(1), windows, null);
                    Check(store.ReadQuota(key, now.AddDays(-1), now.AddDays(1)).Count == 1, "unchanged quota avoids per-refresh writes");
                    store.Record(key, now.AddMinutes(15), windows, null);
                    Check(store.ReadQuota(key, now.AddDays(-1), now.AddDays(1)).Count == 2, "unchanged quota gains fifteen-minute anchor");
                    windows[0].UsedPercent = 21;
                    store.Record(key, now.AddMinutes(16), windows, null);
                    windows[0].ResetsAt = now.AddHours(5);
                    store.Record(key, now.AddMinutes(17), windows, null);
                    Check(store.ReadQuota(key, now.AddDays(-1), now.AddDays(1)).Count == 4, "usage and reset changes create samples");
                    var usage = new TokenUsage { DailyAvailable = true, Daily = new List<DailyTokens> { new DailyTokens { Date = now.UtcDateTime.ToString("yyyy-MM-dd"), Tokens = 100 } } };
                    store.Record(key, now.AddMinutes(17), windows, usage);
                    usage.Daily[0].Tokens = 80;
                    store.Record(key, now.AddMinutes(17), windows, usage);
                    Check(store.ReadTokens(key, "0001-01-01", "9999-12-31").Single().Tokens == 80, "token upsert corrects daily totals without addition");
                    store.Record("other-account", now, windows, usage);
                    Check(store.ReadQuota("unknown", now.AddDays(-1), now.AddDays(1)).Count == 0, "history queries cannot mix accounts");
                    Check(store.Windows(key).Single().WindowId == "codex:300" && store.ReadQuota(key, now.AddDays(-1), now.AddDays(1), "absent").Count == 0,
                        "chart metadata and window queries stay scoped to the account");
                    var csv = Path.Combine(directory, "export.csv");
                    windows[0].Bucket = "=HYPERLINK(\"unsafe\")";
                    store.Record(key, now.AddMinutes(18), windows, null);
                    store.ExportCsv(key, csv, now.AddDays(-1), now.AddDays(1));
                    string contents = File.ReadAllText(csv);
                    Check(contents.Contains("quota,") && contents.Contains("tokens,") && !contents.Contains(key) && !contents.Contains("example.test"), "CSV exports observations without account identity");
                    Check(contents.Contains("'="), "CSV service labels escape spreadsheet formulas");
                    store.Record(key, now.AddDays(-40), new List<QuotaWindow> { new QuotaWindow { Id = "old", Bucket = "codex" } },
                        new TokenUsage { DailyAvailable = true, Daily = new List<DailyTokens> { new DailyTokens { Date = now.AddDays(-40).UtcDateTime.ToString("yyyy-MM-dd"), Tokens = 1 } } });
                    store.SetRetention(30);
                    Check(!store.ReadQuota(key, now.AddDays(-50), now.AddDays(1)).Any(q => q.WindowId == "old") && store.ReadTokens(key, "0001-01-01", "9999-12-31").Count == 1, "retention prunes quota and token history");
                    Check(store.SizeBytes > 0, "history size includes database journal");
                    store.Clear(key);
                    Check(store.ReadQuota(key, now.AddDays(-50), now.AddDays(1)).Count == 0 && store.ReadTokens(key, "0001-01-01", "9999-12-31").Count == 0 &&
                        store.ReadQuota("other-account", now.AddDays(-1), now.AddDays(1)).Count == 1, "clear affects only selected account");
                }
                using (var reopened = new HistoryStore(path))
                    Check(key == reopened.AccountKey("chatgpt", "a@example.test", "new-session"), "database reopen preserves salted identity");
                Check(!System.Text.Encoding.UTF8.GetString(File.ReadAllBytes(path)).Contains("example.test"), "database never persists account email");
                var samples = new List<QuotaSample>();
                foreach (int minute in new[] { 0, 15, 30, 90, 105 }) samples.Add(new QuotaSample { At = now.AddMinutes(minute), WindowId = "codex:300", Remaining = 90 - minute / 10.0, ResetsAt = now.AddHours(5) });
                Check(HistorySeries.Segments(samples).Count == 2, "chart breaks across unobserved thirty-minute gaps");
                samples[4].Remaining = 100;
                Check(HistorySeries.Segments(samples).Count == 3, "chart breaks on replenishment without reset-date change");
                samples[1].ResetsAt = now.AddHours(6);
                Check(HistorySeries.Segments(samples).Count == 5, "chart never joins distinct reset cycles");
                var settings = new SettingsStore(Path.Combine(directory, "settings.json"));
                settings.Save(new Settings { HistoryEnabled = false, HistoryRetentionDays = 90 }); string warning;
                var restored = settings.Load(out warning);
                Check(!restored.Copy().HistoryEnabled && restored.HistoryRetentionDays == 90, "history preferences roundtrip and copy");
                var appearance = new Settings { Language = "en", IndicatorStyle = "Horizontal", ShowTokens = false, ShowTime = false,
                    ShowPace = false, ShowReset = false, HiddenWindows = new[] { "codex:300" }, WindowOrder = new[] { "other:10080", "codex:10080" } };
                settings.Save(appearance); restored = settings.Load(out warning);
                Check(restored.Language == "en" && restored.IndicatorStyle == "Horizontal" && !restored.ShowTokens && !restored.ShowTime && !restored.ShowPace && !restored.ShowReset,
                    "language style and content preferences persist");
                var visible = restored.VisibleWindows(QuotaParser.Parse(Json.Parse(MockServer.Limits)));
                Check(visible.Count == 2 && visible[0].Id == "other:10080" && visible[1].Id == "codex:10080", "content filters and custom order are independent of server order");
                restored.Copy().HiddenWindows[0] = "modified";
                Check(restored.HiddenWindows[0] == "codex:300", "settings copies isolate mutable content arrays");
                Check(Settings.ValidStyle("invalid") == "Rings" && Settings.ValidLanguage("invalid") == "zh-CN", "invalid display preferences fall back safely");
                Check(TextCatalog.Transform("剩余额度", "en") == "Quota left" && TextCatalog.Transform("3 小时 4 分钟后重置", "en") == "3 h 4 min until reset", "English localizes fixed and composed quota labels");
                Check(TextCatalog.Transform("历史记录", "zh-TW") == "歷史記錄" && TextCatalog.Transform("剩余额度", "zh-CN") == "剩余额度", "traditional and simplified Chinese localization");
            }
            finally { DeleteTestDirectory(directory); }
        }
        private static async Task HistoryService()
        {
            var directory = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "test-data", "history-service-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);
            try
            {
                using (var history = new HistoryStore(Path.Combine(directory, "history.sqlite3")))
                {
                    var fake = new FakeConnection(); var factory = new FakeFactory(fake);
                    using (var service = new UsageService(new FixedLocator(Mock("normal")), factory))
                    {
                        service.ConfigureHistory(history, 0, true);
                        await service.RefreshAsync(true); string account = service.State.AccountKey;
                        Check(service.State.Tokens.LifetimeTokens == 1000 && service.State.TokenError == null, "service reads optional token activity");
                        Check(history.ReadQuota(account, DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow).Count == 3 && history.ReadTokens(account, "0001-01-01", "9999-12-31").Count == 1, "service records real responses into SQLite");
                        fake.FailUsage = true; await service.RefreshAsync(true);
                        Check(service.State.Error == null && !service.State.IsStale && service.State.TokensStale && service.State.TokenError != null, "optional token RPC failure does not poison fresh quota");
                        fake.FailUsage = false; fake.Email = "b@example.test";
                        service.ConfigureHistory(history, 0, false); await service.RefreshAsync(true);
                        Check(service.State.AccountKey != account && history.ReadQuota(service.State.AccountKey, DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow).Count == 0, "disabled recording still identifies account without saving observations");
                    }
                    fake = new FakeConnection { DelayUsage = true }; factory = new FakeFactory(fake);
                    using (var service = new UsageService(new FixedLocator(Mock("normal")), factory))
                    {
                        service.ConfigureHistory(history, 0, true);
                        var pending = service.RefreshAsync(true); await fake.UsageEntered.Task;
                        string old = service.State.AccountKey;
                        fake.Email = "c@example.test"; fake.Emit("account/updated");
                        Check(service.State.AccountKey == null && service.State.Tokens == null, "account boundary clears token and history selection immediately");
                        fake.DelayUsage = false; fake.UsageJson = "{\"summary\":{\"lifetimeTokens\":9},\"dailyUsageBuckets\":[]}";
                        fake.DelayedUsage.TrySetResult(Json.Parse("{\"summary\":{\"lifetimeTokens\":99999},\"dailyUsageBuckets\":[{\"startDate\":\"2000-01-01\",\"tokens\":99999}]}"));
                        await pending; await Task.Delay(100);
                        Check(service.State.Tokens != null && service.State.Tokens.LifetimeTokens == 9 && service.State.AccountKey != old &&
                            history.ReadTokens(old, "2000-01-01", "2000-01-01").Count == 0, "late token response is excluded from state and historical writes");
                    }
                    fake = new FakeConnection { DelayUsage = true }; factory = new FakeFactory(fake);
                    using (var service = new UsageService(new FixedLocator(Mock("normal")), factory))
                    {
                        service.RequestTimeoutMilliseconds = 100;
                        await service.RefreshAsync(true);
                        Check(service.State.Windows.Count == 3 && service.State.Error == null && !service.State.IsStale && service.State.TokenError != null,
                            "optional token timeout retains fresh quota");
                    }
                    fake = new FakeConnection { Email = "" }; factory = new FakeFactory(fake);
                    using (var service = new UsageService(new FixedLocator(Mock("normal")), factory))
                    {
                        service.ConfigureHistory(history, 0, true); await service.RefreshAsync(true);
                        string anonymous = service.State.AccountKey;
                        fake.Emit("account/updated"); await Task.Delay(100);
                        Check(service.State.AccountKey != null && service.State.AccountKey != anonymous,
                            "explicit anonymous account boundary rotates persisted history partition");
                    }
                    fake = new FakeConnection { DelayUsage = true, Email = "new@example.test" }; factory = new FakeFactory(fake);
                    using (var service = new UsageService(new FixedLocator(Mock("normal")), factory))
                    {
                        var pending = service.RefreshAsync(true); await fake.UsageEntered.Task;
                        service.ConfigureHistory(history, 0, true); fake.DelayUsage = false;
                        fake.DelayedUsage.TrySetResult(Json.Parse(fake.UsageJson)); await pending;
                        for (int attempt = 0; attempt < 100 && service.State.AccountKey == null; attempt++) await Task.Delay(10);
                        Check(service.State.AccountKey != null && history.ReadQuota(service.State.AccountKey, DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow).Count == 3,
                            "enabling history during a refresh schedules a fresh account-bound sample");
                    }
                }
            }
            finally { DeleteTestDirectory(directory); }
        }
    }
}
