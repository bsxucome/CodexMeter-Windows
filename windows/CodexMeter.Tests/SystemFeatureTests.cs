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
        private const string AllowanceJson = "{\"rateLimits\":{\"credits\":{\"hasCredits\":true,\"unlimited\":false,\"balance\":\"25.50\"}},\"rateLimitResetCredits\":{\"availableCount\":3,\"credits\":[{\"id\":\"one\",\"resetType\":\"codexRateLimits\",\"status\":\"available\",\"grantedAt\":1000,\"expiresAt\":2000},{\"id\":\"one\",\"status\":\"available\"},{\"id\":\"used\",\"resetType\":\"codexRateLimits\",\"status\":\"redeemed\"}]}}";
        private static void SystemFeatures()
        {
            var data = Json.Parse(AllowanceJson);
            var credits = CreditBalance.Parse(data); var resets = ResetCredits.Parse(data);
            Check(credits.Balance == "25.50" && credits.HasCredits == true && credits.Unlimited == false, "optional balance retains exact service decimal and flags");
            Check(CreditBalance.Parse(Json.Parse("{}")) == null && ResetCredits.Parse(Json.Parse("{}")) == null, "missing balances and reset summaries remain unavailable");
            Check(ResetCredits.Parse(Json.Parse("{\"rateLimitResetCredits\":null}")) == null, "null reset summary does not become a zero count");
            var zero = ResetCredits.Parse(Json.Parse("{\"rateLimitResetCredits\":{\"availableCount\":0,\"credits\":[]}}"));
            Check(zero != null && zero.AvailableCount == 0 && zero.DetailsAvailable, "confirmed zero differs from missing reset information");
            Check(resets.AvailableCount == 3 && resets.Credits.Count == 2 && resets.DetailsAvailable, "reset count stays authoritative despite partial and duplicate detail rows");
            var countOnly = ResetCredits.Parse(Json.Parse("{\"rateLimitResetCredits\":{\"availableCount\":2,\"credits\":null}}"));
            Check(countOnly.AvailableCount == 2 && !countOnly.DetailsAvailable, "count-only reset summaries preserve unavailable detail status");
            Check(ResetCredits.Parse(Json.Parse("{\"rateLimitResetCredits\":{\"availableCount\":-1}}")) == null &&
                ResetCredits.Parse(Json.Parse("{\"rateLimitResetCredits\":{\"availableCount\":1.5}}")) == null, "invalid reset counts rejected");
            var epoch = new DateTimeOffset(1970, 1, 1, 0, 0, 0, TimeSpan.Zero);
            var reset = resets.Credits[0];
            Check(reset.RemainingLifetime(epoch.AddSeconds(1000)) == 100 && reset.RemainingLifetime(epoch.AddSeconds(1500)) == 50 &&
                reset.RemainingLifetime(epoch.AddSeconds(2000)) == 0, "reset lifetime follows grant and expiration boundaries");
            Check(reset.Usable(epoch.AddSeconds(1999)) && !reset.Usable(epoch.AddSeconds(2000)) && !resets.Credits[1].Usable(epoch), "expired and consumed reset rows cannot appear usable");
            reset.ExpiresAt = reset.GrantedAt;
            Check(!reset.RemainingLifetime(epoch).HasValue, "invalid reset lifetime does not fabricate a progress bar");
            reset.ExpiresAt = null;
            Check(!reset.RemainingLifetime(epoch).HasValue && reset.Usable(epoch), "nonexpiring reset rows omit lifetime progress");
            resets.AvailableCount = 0;
            Check(resets.UsableCredits(epoch).Count == 0, "an authoritative zero suppresses contradictory available detail rows");
            resets.AvailableCount = 1; resets.Credits.Add(new ResetCredit { Id = "soon", Status = "available", ResetType = "codexRateLimits", ExpiresAt = epoch.AddHours(1) });
            Check(resets.UsableCredits(epoch).Single().Id == "soon", "usable reset rows respect the declared count and nearest expiration");
            var badBalance = CreditBalance.Parse(Json.Parse("{\"rateLimits\":{\"credits\":{\"balance\":\"-2\"}}}"));
            Check(badBalance == null, "malformed or negative credit balances remain unavailable");
            credits = CreditBalance.Parse(Json.Parse("{\"rateLimits\":{\"credits\":{\"balance\":\"99\"}},\"rateLimitsByLimitId\":{\"codex\":{\"credits\":{\"balance\":\"0\",\"hasCredits\":false}}}}"));
            Check(credits.Balance == "0" && credits.HasCredits == false, "standard bucket balance wins over the compatibility snapshot");
            var monitor = new ScreenRect(0, 0, 1920, 1080); var taskbar = new ScreenRect(0, 1032, 1920, 1080);
            Check(WindowGeometry.CoversTaskbarMonitor(monitor, monitor, taskbar), "primary fullscreen window hides its taskbar overlay");
            Check(!WindowGeometry.CoversTaskbarMonitor(new ScreenRect(1920, 0, 3840, 1080), new ScreenRect(1920, 0, 3840, 1080), taskbar), "fullscreen on a second monitor does not hide primary indicator");
            Check(!WindowGeometry.CoversTaskbarMonitor(new ScreenRect(0, 0, 1920, 1032), monitor, taskbar), "maximized working-area window is not fullscreen");
            Check(WindowGeometry.CoversTaskbarMonitor(new ScreenRect(-1920, 0, 0, 1080), new ScreenRect(-1920, 0, 0, 1080), new ScreenRect(-1920, 1032, 0, 1080)), "fullscreen geometry supports negative monitor coordinates");
            Check(WindowGeometry.CoversTaskbarMonitor(new ScreenRect(0, 0, 3840, 1080), monitor, taskbar), "a fullscreen window spanning both monitors still covers the primary taskbar");
            Check(WindowGeometry.IsShellSurface("Windows.UI.Core.CoreWindow", "StartMenuExperienceHost") && WindowGeometry.IsShellSurface("Windows.UI.Core.CoreWindow", "SearchHost"),
                "Start and search hosts are excluded from fullscreen hiding");
            Check(!WindowGeometry.IsShellSurface("Windows.UI.Core.CoreWindow", "ExamplePlayer"), "ordinary UWP fullscreen applications are not excluded as shell surfaces");
            NotificationCases(); StartupCases(); ExtendedSettings(); SlidingResetCases();
        }
        private static void NotificationCases()
        {
            var now = DateTimeOffset.UtcNow; var engine = new QuotaNotifications(); var settings = new Settings();
            var window = new QuotaWindow { Id = "codex:300", Bucket = "codex", UsedPercent = 85, DurationMinutes = 300, ResetsAt = now.AddMinutes(200) };
            var state = new UsageState { Windows = new List<QuotaWindow> { window }, UpdatedAt = now, SessionId = "account-a" };
            Check(engine.Evaluate(state, settings, now).Count == 0, "notifications are opt-in for existing installations");
            settings.Notifications = true;
            var notices = engine.Evaluate(state, settings, now);
            Check(notices.Count == 1 && notices[0].Kind == "Low", "simultaneous low and over-pace transitions produce one prioritized alert");
            Check(engine.Evaluate(state, settings, now).Count == 0, "rendering the same snapshot never duplicates a notice");
            state.UpdatedAt = now.AddMinutes(1);
            Check(engine.Evaluate(state, settings, state.UpdatedAt.Value).Count == 0, "sustained unsafe quota is announced only once");
            window.UsedPercent = 0; state.UpdatedAt = now.AddMinutes(2); engine.Evaluate(state, settings, state.UpdatedAt.Value);
            window.UsedPercent = 90; state.UpdatedAt = now.AddMinutes(3);
            Check(engine.Evaluate(state, settings, state.UpdatedAt.Value).Count == 0, "reentry during cooldown is deferred");
            state.UpdatedAt = now.AddMinutes(31);
            Check(engine.Evaluate(state, settings, state.UpdatedAt.Value).Count == 1, "pending unsafe reentry is delivered after cooldown with fresh data");
            state.IsStale = true; state.SessionId = "account-b"; state.UpdatedAt = now;
            Check(engine.Evaluate(state, settings, now).Count == 0 && !QuotaNotifications.StillApplies(notices[0], state, settings, now), "stale data and queued notices from another account cannot alert");
            state.IsStale = false; state.IsLoading = true;
            Check(engine.Evaluate(state, settings, now).Count == 0, "in-flight account refresh does not alert from prior data");
            state.IsLoading = false; state.UpdatedAt = now.AddMinutes(-3);
            Check(engine.Evaluate(state, settings, now).Count == 0, "old successful snapshots cannot trigger notifications");
            state.UpdatedAt = now.AddMinutes(1);
            Check(engine.Evaluate(state, settings, now).Count == 0, "future timestamps cannot trigger notifications");
            state.UpdatedAt = now; settings.NotifyLowQuota = false; window.ResetsAt = null;
            Check(engine.Evaluate(state, settings, now).Count == 0, "missing reset timing cannot invent an over-pace alert");
            window.ResetsAt = now.AddMinutes(200); state.UpdatedAt = now.AddSeconds(1);
            Check(engine.Evaluate(state, settings, now.AddSeconds(1)).Single().Kind == "Pace", "over-pace notifications can be enabled independently");
            settings.NotifyLowQuota = true; settings.NotifyOverPace = false; state.UpdatedAt = now.AddSeconds(2); window.UsedPercent = 80;
            Check(engine.Evaluate(state, settings, now.AddSeconds(2)).Single().Kind == "Low", "low-quota threshold is inclusive and independent of pacing");
            window.UsedPercent = 0;
            Check(!QuotaNotifications.StillApplies(notices[0], state, settings, now), "recovery invalidates a queued unsafe notice");
            state.SessionId = "expired"; state.UpdatedAt = now; window.UsedPercent = 90; window.ResetsAt = now.AddSeconds(-1);
            Check(engine.Evaluate(state, settings, now).Count == 0, "already-expired quota windows do not send low-quota alerts");
            state.SessionId = "cycle"; window.ResetsAt = now.AddHours(1); settings.NotificationCooldownMinutes = 1;
            engine.Evaluate(state, settings, now);
            state.UpdatedAt = now.AddMinutes(2); window.ResetsAt = now.AddHours(1).AddSeconds(30);
            Check(engine.Evaluate(state, settings, now.AddMinutes(2)).Count == 0, "reset timestamp jitter cannot retrigger an unsafe state");
            state.UpdatedAt = now.AddMinutes(4); window.ResetsAt = now.AddHours(6);
            Check(engine.Evaluate(state, settings, now.AddMinutes(4)).Count == 1, "a confirmed new quota cycle can alert without an observed safe intermediate snapshot");
            Check(!QuotaNotifications.StillApplies(notices[0], new UsageState { SessionId = "account-a", Windows = new[] { window }, IsLoading = true }, settings, now),
                "queued alerts wait while account data is being refreshed");
        }
        private sealed class StartupMemory : IStartupValueStore
        {
            public string Value; public byte[] Approval; public int Writes;
            public string ReadCommand() { return Value; }
            public byte[] ReadApproval() { return Approval; }
            public void WriteCommand(string value) { Writes++; Value = value; }
        }
        private static void StartupCases()
        {
            var memory = new StartupMemory(); var registration = new StartupRegistration(memory, @"C:\Apps with spaces\CodexMeter.exe");
            Check(!registration.Read().Registered && memory.Writes == 0, "startup status reads do not register or alter system startup");
            registration.Set(true);
            Check(memory.Value == "\"C:\\Apps with spaces\\CodexMeter.exe\"" && registration.Read().Registered, "startup command quotes the complete portable executable path");
            memory.Approval = new byte[12]; memory.Approval[0] = 3;
            Check(registration.Read().DisabledByWindows, "system-disabled startup remains visible as disabled");
            registration.Set(true);
            Check(memory.Approval[0] == 3 && registration.Read().DisabledByWindows, "registering never overwrites Windows startup approval");
            memory.Approval = new byte[1];
            Check(registration.Read().ApprovalUnknown, "unrecognized startup approval is reported as unknown");
            memory.Value = "\"D:\\Previous\\CodexMeter.exe\"";
            Check(!registration.Read().Registered && registration.Read().OtherLocation, "startup entries pointing at an old release are detected");
            string original = registration.CaptureCommand(); registration.Set(true); registration.RestoreCommand(original);
            Check(memory.Value == original, "failed settings save can restore the exact prior startup command");
            registration.Set(false);
            Check(memory.Value == null && !registration.Read().Registered, "disabling removes only the application's startup value");
        }
        private static void ExtendedSettings()
        {
            string directory = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "test-data", "system-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);
            try
            {
                var store = new SettingsStore(Path.Combine(directory, "settings.json")); string warning;
                File.WriteAllText(store.FilePath, "{\"sectionOrder\":[\"Tokens\",\"Tokens\",\"unknown\"],\"notificationThreshold\":999,\"notificationCooldownMinutes\":-1}");
                var settings = store.Load(out warning);
                Check(!settings.Notifications && !settings.LaunchAtLogin && settings.ShowResetCredits, "old settings migrate with alerts and startup disabled");
                Check(settings.SectionOrder.First() == "Tokens" && settings.SectionOrder.Length == 5 && settings.NotificationThreshold == 99 && settings.NotificationCooldownMinutes == 1,
                    "section order is repaired and notification settings are bounded");
                settings.Notifications = true; settings.ShowCredits = false; settings.NotificationThreshold = 12; settings.LaunchAtLogin = true;
                store.Save(settings); var restored = store.Load(out warning);
                Check(restored.Notifications && !restored.ShowCredits && restored.NotificationThreshold == 12 && restored.LaunchAtLogin && restored.SectionOrder.First() == "Tokens",
                    "system and section preferences roundtrip without losing prior settings");
                var copy = restored.Copy(); copy.SectionOrder[0] = "Quota";
                Check(restored.SectionOrder[0] == "Tokens", "section ordering copies cannot mutate saved preferences");
            }
            finally { Directory.Delete(directory, true); }
        }
        private static void SlidingResetCases()
        {
            var now = DateTimeOffset.UtcNow;
            var samples = new[] { Point(now, 100, now.AddHours(5), 300), Point(now.AddMinutes(15), 100, now.AddHours(5).AddMinutes(15), 300),
                Point(now.AddMinutes(30), 100, now.AddHours(5).AddMinutes(30), 300) };
            var normalized = HistoryAnalytics.Normalize(samples);
            Check(HistoryAnalytics.Cycles(samples).Count == 1 && normalized.Last().ResetsAt == samples[0].ResetsAt && samples[2].ResetsAt != samples[0].ResetsAt,
                "unused sliding reset deadlines normalize without mutating raw historical samples");
            var live = new QuotaWindow { DurationMinutes = 300, ResetsAt = now.AddHours(5), CycleResetsAt = now.AddHours(4) };
            Check(HistoryCalendar.Cycle(live, now).To == live.CycleResetsAt && live.ResetsAt == now.AddHours(5),
                "current history cycle uses its stable anchor while the official reset display stays untouched");
            live.CycleResetsAt = now.AddHours(-1);
            Check(HistoryCalendar.Cycle(live, now).To == now && HistoryCalendar.Cycle(live, now).From == now.AddHours(-5),
                "long-unused windows retain visible rolling history after their first anchor expires");
            var consumed = samples.Concat(new[] { Point(now.AddMinutes(45), 90, now.AddHours(6), 300) }).ToArray();
            Check(HistoryAnalytics.Cycles(consumed).Count == 2, "a changed deadline after actual consumption is retained");
            string directory = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "test-data", "sliding-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);
            try
            {
                using (var store = new HistoryStore(Path.Combine(directory, "history.sqlite3")))
                {
                    var window = new QuotaWindow { Id = "codex:300", Bucket = "codex", UsedPercent = 0, DurationMinutes = 300, ResetsAt = now.AddHours(5) };
                    store.Record("one", now, new[] { window }, null); window.ResetsAt = now.AddHours(5).AddMinutes(1);
                    store.Record("one", now.AddMinutes(1), new[] { window }, null);
                    Check(store.ReadQuota("one", now.AddDays(-1), now.AddDays(1)).Count == 1, "idle reset jitter does not create duplicate database samples");
                    window.ResetsAt = now.AddHours(5).AddMinutes(15); store.Record("one", now.AddMinutes(15), new[] { window }, null);
                    Check(Math.Abs((store.ReadQuota("one", now.AddDays(-1), now.AddDays(1)).Last().ResetsAt.Value - now.AddHours(5)).TotalMilliseconds) < 1,
                        "fifteen-minute anchors retain the normalized unused cycle identity");
                }
            }
            finally { Directory.Delete(directory, true); }
        }
        private static async Task AllowancesService()
        {
            var fake = new FakeConnection { LimitsJson = AllowanceJson };
            using (var service = new UsageService(new FixedLocator(Mock("normal")), new FakeFactory(fake)))
            {
                await service.RefreshAsync(true);
                var state = service.State;
                Check(state.Credits.Balance == "25.50" && state.ResetCredits.AvailableCount == 3 && state.Error == null && state.Windows.Count == 0,
                    "balance-only responses remain usable without fabricated quota windows");
                string session = state.SessionId; fake.FailLimits = true;
                await service.RefreshAsync(true);
                Check(service.State.IsStale && service.State.ResetCredits.AvailableCount == 3, "refresh failure retains and marks optional allowances stale");
                fake.FailLimits = false; fake.LimitsJson = MockServer.Limits; fake.Email = "second@example.test";
                await service.RefreshAsync(true);
                Check(service.State.SessionId != session && service.State.Credits == null && service.State.ResetCredits == null, "account changes clear allowances and rotate notification scope without history");
                fake.LimitsJson = AllowanceJson; await service.RefreshAsync(true);
                fake.LoggedIn = false; await service.RefreshAsync(true);
                Check(service.State.Credits == null && service.State.ResetCredits == null, "logout clears both balance and reset summaries");
                Check(!fake.Methods.Any(m => m.IndexOf("consume", StringComparison.OrdinalIgnoreCase) >= 0), "read-only reset display never redeems an opportunity");
            }
        }
    }
}
