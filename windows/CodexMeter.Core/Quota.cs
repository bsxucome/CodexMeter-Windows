using System;
using System.Collections.Generic;
using System.Linq;

namespace CodexMeter.Core
{
    public enum Attention { Normal, Warning, Critical, Unknown }
    public sealed class QuotaWindow
    {
        public string Id { get; set; }
        public string Bucket { get; set; }
        public double UsedPercent { get; set; }
        public double? DurationMinutes { get; set; }
        public DateTimeOffset? ResetsAt { get; set; }
        public DateTimeOffset? CycleResetsAt { get; set; }
        public double RemainingPercent { get { return Math.Max(0, Math.Min(100, 100 - UsedPercent)); } }
        public double? TimeRemainingPercent(DateTimeOffset now)
        {
            if (!DurationMinutes.HasValue || DurationMinutes <= 0 || !ResetsAt.HasValue) return null;
            return Math.Max(0, Math.Min(100, (ResetsAt.Value - now).TotalMinutes / DurationMinutes.Value * 100));
        }
        public Attention State(DateTimeOffset now)
        {
            if (RemainingPercent < 20) return Attention.Critical;
            var time = TimeRemainingPercent(now);
            return time.HasValue && RemainingPercent < time.Value ? Attention.Warning : Attention.Normal;
        }
        public string Title
        {
            get
            {
                string duration = !DurationMinutes.HasValue ? "额度窗口" :
                    DurationMinutes == 300 ? "5 小时" : DurationMinutes == 10080 ? "每周" :
                    DurationMinutes.Value % 1440 == 0 ? (DurationMinutes.Value / 1440).ToString("0") + " 天" :
                    DurationMinutes.Value % 60 == 0 ? (DurationMinutes.Value / 60).ToString("0") + " 小时" :
                    DurationMinutes.Value.ToString("0.#") + " 分钟";
                return string.Equals(Bucket, "codex", StringComparison.OrdinalIgnoreCase) ? duration : Bucket + " · " + duration;
            }
        }
        public string Countdown(DateTimeOffset now)
        {
            if (!ResetsAt.HasValue) return "重置时间不可用";
            var remaining = ResetsAt.Value - now;
            if (remaining <= TimeSpan.Zero) return "等待额度重置";
            if (remaining.TotalDays >= 1) return string.Format("{0} 天 {1} 小时后重置", (int)remaining.TotalDays, remaining.Hours);
            if (remaining.TotalHours >= 1) return string.Format("{0} 小时 {1} 分钟后重置", (int)remaining.TotalHours, remaining.Minutes);
            return Math.Max(1, (int)Math.Ceiling(remaining.TotalMinutes)) + " 分钟后重置";
        }
    }

    public static class QuotaParser
    {
        public static List<QuotaWindow> Parse(Dictionary<string, object> result)
        {
            var windows = new List<QuotaWindow>();
            var buckets = Json.Object(result, "rateLimitsByLimitId");
            if (buckets != null && buckets.Count > 0)
            {
                foreach (var bucket in buckets)
                    AddSnapshot(windows, bucket.Value as Dictionary<string, object>, bucket.Key);
            }
            else AddSnapshot(windows, Json.Object(result, "rateLimits"), "codex");
            return windows.OrderBy(w => w.DurationMinutes ?? double.MaxValue).ThenBy(w => w.Id, StringComparer.Ordinal).ToList();
        }
        private static void AddSnapshot(List<QuotaWindow> windows, Dictionary<string, object> snapshot, string fallback)
        {
            if (snapshot == null) return;
            var id = Json.Text(snapshot, "limitId") ?? fallback;
            var name = Json.Text(snapshot, "limitName");
            if (string.IsNullOrWhiteSpace(name)) name = id;
            foreach (var slot in new[] { "primary", "secondary" })
            {
                var data = Json.Object(snapshot, slot);
                var used = Json.Number(data, "usedPercent");
                if (!used.HasValue) continue;
                var duration = Json.Number(data, "windowDurationMins");
                if (duration <= 0 || duration > 52560000) duration = null;
                DateTimeOffset? reset = null;
                var seconds = Json.Number(data, "resetsAt");
                if (seconds.HasValue)
                {
                    try { reset = new DateTimeOffset(1970, 1, 1, 0, 0, 0, TimeSpan.Zero).AddSeconds(seconds.Value); }
                    catch (ArgumentOutOfRangeException) { }
                }
                // Duration, not primary/secondary position, identifies a quota across slot swaps.
                windows.Add(new QuotaWindow {
                    Id = id + ":" + (duration.HasValue ? duration.Value.ToString("R", System.Globalization.CultureInfo.InvariantCulture) : slot),
                    Bucket = name, UsedPercent = used.Value, DurationMinutes = duration, ResetsAt = reset
                });
            }
        }
        public static QuotaWindow Select(IList<QuotaWindow> windows, string selected)
        {
            return windows.FirstOrDefault(w => w.Id == selected) ??
                windows.FirstOrDefault(w => w.Id == "codex:10080") ??
                windows.OrderBy(w => w.RemainingPercent).FirstOrDefault();
        }
    }
}
