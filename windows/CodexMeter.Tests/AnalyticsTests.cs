using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using CodexMeter.Core;

namespace CodexMeter.Tests
{
    internal static partial class Program
    {
        private static QuotaSample Point(DateTimeOffset at, double remaining, DateTimeOffset reset, double duration)
        { return new QuotaSample { At = at, Remaining = remaining, WindowId = "codex:300", Title = "5 小时", ResetsAt = reset, DurationMinutes = duration }; }
        private static void Analytics()
        {
            var now = new DateTimeOffset(2026, 10, 18, 12, 0, 0, TimeSpan.Zero);
            var month = HistoryCalendar.Natural(new DateTime(2024, 2, 15), true, TimeZoneInfo.Utc);
            Check((month.To - month.From).TotalDays == 29 && month.From.Day == 1 && month.To.Month == 3, "calendar months preserve leap-year boundaries");
            var week = HistoryCalendar.Natural(new DateTime(2026, 10, 18), false, TimeZoneInfo.Utc);
            Check(week.From.DayOfWeek == DayOfWeek.Monday && week.From.Day == 12 && week.To.Day == 19, "calendar week navigation includes Sunday in its Monday week");
            var pacific = TimeZoneInfo.FindSystemTimeZoneById("Pacific Standard Time");
            var dstWeek = HistoryCalendar.Natural(new DateTime(2026, 3, 8), false, pacific);
            Check((dstWeek.To - dstWeek.From).TotalHours == 167, "calendar boundaries use independent timezone offsets across DST");
            var five = new QuotaWindow { DurationMinutes = 300, ResetsAt = now.AddHours(2) };
            var cycle = HistoryCalendar.Cycle(five, now);
            Check((cycle.To - cycle.From).TotalMinutes == 300 && cycle.To == five.ResetsAt, "current cycle respects the selected window duration");
            five.ResetsAt = null;
            Check((HistoryCalendar.Cycle(five, now).To - HistoryCalendar.Cycle(five, now).From).TotalHours == 5, "missing reset keeps returned duration without inventing a reset");
            var tokens = new List<DailyTokens> {
                new DailyTokens { Date = "2025-12-31", Tokens = 2 }, new DailyTokens { Date = "2026-01-01", Tokens = 3 },
                new DailyTokens { Date = "2026-01-01", Tokens = 4 }, new DailyTokens { Date = "2026-01-05", Tokens = 8 } };
            var weeks = TokenSeries.Group(tokens, "Week");
            Check(weeks.Count == 2 && weeks[0].From.Year == 2025 && weeks[0].Tokens == 6 && weeks[0].ObservedDays == 2, "weekly token aggregation spans year boundaries and corrects duplicate dates");
            var months = TokenSeries.Group(tokens, "Month");
            Check(months.Count == 2 && months[0].Tokens == 2 && months[1].Tokens == 12 && months[1].To.Month == 2, "all-time token aggregation uses natural months");
            Check(TokenSeries.Group(new DailyTokens[0], "Day").Count == 0 && TokenSeries.Group(tokens, "Day").Count == 3, "token aggregation never fabricates missing days");
            var huge = TokenSeries.Group(new[] { new DailyTokens { Date = "2026-01-01", Tokens = long.MaxValue }, new DailyTokens { Date = "2026-01-02", Tokens = long.MaxValue } }, "Month");
            Check(huge[0].Tokens == (decimal)long.MaxValue * 2, "grouped token totals do not overflow Int64");
            var reset = now.AddHours(2);
            var points = new List<QuotaSample> { Point(now.AddMinutes(-30), 9, reset, 300), Point(now.AddMinutes(-15), 6, reset, 300), Point(now, 3, reset, 300) };
            var analysis = HistoryAnalytics.Analyze(points, now.AddHours(-1), now, now);
            Check(analysis.ExhaustsAt == now.AddMinutes(15), "eligible monotonic segment produces an exhaustion estimate before reset");
            Check(!HistoryAnalytics.Analyze(points.Skip(1), now.AddHours(-1), now, now).ExhaustsAt.HasValue, "two samples do not qualify for an estimate");
            var shortSpan = points.Select((p, i) => Point(now.AddMinutes(-10 + i * 5), p.Remaining, reset, 300)).ToList();
            Check(!HistoryAnalytics.Analyze(shortSpan, now.AddHours(-1), now, now).ExhaustsAt.HasValue, "estimate requires at least fifteen minutes of observation");
            var smallDrop = points.Select((p, i) => Point(p.At, 3 - i * .5, reset, 300)).ToList();
            Check(!HistoryAnalytics.Analyze(smallDrop, now.AddHours(-1), now, now).ExhaustsAt.HasValue, "estimate requires at least two percentage points of consumption");
            var replenished = points.Select(p => Point(p.At, p.Remaining, reset, 300)).ToList(); replenished[1].Remaining = 10;
            Check(!HistoryAnalytics.Analyze(replenished, now.AddHours(-1), now, now).ExhaustsAt.HasValue, "replenishment restarts estimate eligibility");
            var beforeReset = points.Select(p => Point(p.At, p.Remaining, now.AddMinutes(10), 300)).ToList();
            Check(!HistoryAnalytics.Analyze(beforeReset, now.AddHours(-1), now, now).ExhaustsAt.HasValue, "exhaustion after official reset is suppressed");
            Check(!HistoryAnalytics.Analyze(points, now.AddHours(-1), now, now.AddMinutes(16)).ExhaustsAt.HasValue, "stale observations do not produce estimates");
            var missing = points.Select(p => Point(p.At, p.Remaining, reset, 300)).ToList(); missing.ForEach(p => p.ResetsAt = null);
            Check(!HistoryAnalytics.Analyze(missing, now.AddHours(-1), now, now).ExhaustsAt.HasValue, "missing reset does not invent a prediction deadline");
            var jitter = points.Select((p, i) => Point(p.At, p.Remaining, reset.AddSeconds(i * 20), 300)).ToList();
            Check(HistoryAnalytics.Cycles(jitter).Count == 1 && HistoryAnalytics.Analyze(jitter, now.AddHours(-1), now, now).ExhaustsAt.HasValue, "small reset timestamp jitter does not split a cycle");
            var zero = now.AddHours(-2);
            var across = new List<QuotaSample> { Point(zero, 100, zero.AddHours(1), 60), Point(zero.AddMinutes(30), 20, zero.AddHours(1), 60),
                Point(zero.AddHours(1), 100, zero.AddHours(2), 60), Point(zero.AddMinutes(90), 40, zero.AddHours(2), 60) };
            analysis = HistoryAnalytics.Analyze(across, zero, zero.AddMinutes(90), now);
            Check(analysis.Consumed == 140 && !analysis.IsLowerBound, "observed consumption accumulates independent reset cycles beyond one hundred percent");
            analysis = HistoryAnalytics.Analyze(across, zero.AddMinutes(1), zero.AddMinutes(90), now);
            Check(analysis.Consumed == 60 && analysis.IsLowerBound, "clipped leading cycle uses its first sample and reports a lower bound");
            var gap = new List<QuotaSample> { Point(now.AddHours(-2), 90, reset, 300), Point(now.AddHours(-1), 80, reset, 300), Point(now, 70, reset, 300) };
            analysis = HistoryAnalytics.Analyze(gap, now.AddHours(-2), now, now);
            Check(analysis.Consumed == 20 && analysis.IsLowerBound && !analysis.ExhaustsAt.HasValue, "unobserved gaps retain only a consumption lower bound and break prediction segments");
            bool mixedRejected = false; var mixed = points.Select(p => Point(p.At, p.Remaining, reset, 300)).ToList(); mixed[1].WindowId = "other";
            try { HistoryAnalytics.Analyze(mixed, now.AddHours(-1), now, now); } catch (ArgumentException) { mixedRejected = true; }
            Check(mixedRejected, "consumption never adds different quota windows together");
            var stair = new[] { 100.0, 96, 90, 30, 20 }.Select((value, i) => Point(now.AddMinutes(i * 15), value, reset, 300)).ToList();
            double[] smooth = HistoryAnalytics.Smooth(stair), tangent = HistoryAnalytics.Tangents(stair, smooth);
            Check(smooth[0] == 100 && smooth.Last() == 20 && stair[2].Remaining == 90, "trend smoothing preserves endpoints and leaves source samples unchanged");
            bool monotonic = true;
            for (int i = 0; i < stair.Count - 1; i++)
            {
                double prior = smooth[i], minutes = (stair[i + 1].At - stair[i].At).TotalMinutes;
                for (int step = 1; step <= 100; step++)
                {
                    double t = step / 100.0, u = 1 - t;
                    double value = u * u * u * smooth[i] + 3 * u * u * t * (smooth[i] + tangent[i] * minutes / 3) +
                        3 * u * t * t * (smooth[i + 1] - tangent[i + 1] * minutes / 3) + t * t * t * smooth[i + 1];
                    if (value > prior + .000001 || value < smooth[i + 1] - .000001) monotonic = false;
                    prior = value;
                }
            }
            Check(monotonic, "monotone Bezier trend never overshoots sampled quota");
        }
        private static void Appearance()
        {
            var baseline = new IndicatorAppearance();
            Check(baseline.RingSize == 18 && baseline.OuterStroke == 3 && baseline.InnerStroke == 2 && baseline.TimeColor == "Blue", "accepted ring appearance remains the default");
            var bounded = new IndicatorAppearance { RingSize = 1, OuterStroke = 99, InnerStroke = 99, RingGap = 99, FontSize = 99, CaptionSize = 99, Spacing = -20, BarWidth = double.NaN, TimeColor = "invalid" }.Bounded();
            Check(bounded.RingSize == 16 && bounded.OuterStroke == 4 && bounded.InnerStroke == 3 && bounded.FontSize == 15 && bounded.Spacing == 0 && bounded.TimeColor == "Blue", "appearance clamps unsafe or nonfinite values");
            Check((bounded.RingSize - bounded.OuterStroke) / 2 - bounded.RingGap >= bounded.InnerStroke / 2 + 1 && bounded.FontSize + bounded.CaptionSize <= 23,
                "dependent appearance bounds keep inner ring and two text rows inside the taskbar height");
            var exported = IndicatorAppearance.Preset("Bold").Export(); var imported = IndicatorAppearance.Import(exported);
            Check(imported.RingSize == 22 && imported.OuterStroke == 4 && !exported.Contains("account") && !exported.Contains("cliPath"), "appearance JSON roundtrip excludes account and application settings");
            bool rejected = false;
            try { IndicatorAppearance.Import("{\"format\":\"CodexMeter.Appearance\",\"version\":2,\"appearance\":{}}"); } catch (InvalidDataException) { rejected = true; }
            Check(rejected, "unsupported appearance schema does not overwrite preferences");
            rejected = false; try { IndicatorAppearance.Import(new string('x', 16385)); } catch (InvalidDataException) { rejected = true; }
            Check(rejected, "appearance imports are bounded before parsing");
            var copy = new Settings { ShowWeeklyHistory = false, Appearance = IndicatorAppearance.Preset("Compact") };
            copy.Copy().Appearance.RingSize = 24;
            Check(copy.Appearance.RingSize == 16, "editing appearance copies cannot mutate saved settings");
            var directory = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "test-data", "appearance-" + Guid.NewGuid().ToString("N"));
            try
            {
                var store = new SettingsStore(Path.Combine(directory, "settings.json")); store.Save(copy); string warning;
                var restored = store.Load(out warning);
                Check(!restored.ShowWeeklyHistory && restored.Appearance.RingSize == 16 && restored.Appearance.FontSize == 11, "weekly chart preference and bounded appearance persist");
                using (var history = new HistoryStore(Path.Combine(directory, "history.sqlite3")))
                {
                    var now = DateTimeOffset.UtcNow;
                    history.Record("account", now, new List<QuotaWindow> { new QuotaWindow { Id = "codex:300", Bucket = "codex", UsedPercent = 20 } },
                        new TokenUsage { DailyAvailable = true, Daily = new List<DailyTokens> { new DailyTokens { Date = "2000-01-01", Tokens = 42 } } });
                    var csv = Path.Combine(directory, "independent.csv");
                    history.ExportCsv("account", csv, now.AddMinutes(-1), now.AddMinutes(1), "2000-01-01", "2000-01-01");
                    string data = File.ReadAllText(csv);
                    Check(data.Contains("observed_unix_seconds,observed_at_local") && data.Contains("2000-01-01") && data.Contains("42"), "CSV includes raw and local timestamps plus independent token range");
                }
            }
            finally { DeleteTestDirectory(directory); }
        }
    }
}
