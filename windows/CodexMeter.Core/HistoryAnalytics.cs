using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace CodexMeter.Core
{
    public sealed class HistoryInterval
    {
        public DateTimeOffset From { get; set; }
        public DateTimeOffset To { get; set; }
    }
    public static class HistoryCalendar
    {
        public static DateTime WeekStart(DateTime day)
        { return day.Date.AddDays(-((7 + (int)day.DayOfWeek - (int)DayOfWeek.Monday) % 7)); }
        private static DateTimeOffset Local(DateTime day, TimeZoneInfo zone)
        {
            day = DateTime.SpecifyKind(day, DateTimeKind.Unspecified);
            while (zone.IsInvalidTime(day)) day = day.AddMinutes(1);
            return new DateTimeOffset(day, zone.GetUtcOffset(day));
        }
        public static HistoryInterval Natural(DateTime anchor, bool month, TimeZoneInfo zone)
        {
            DateTime first = month ? new DateTime(anchor.Year, anchor.Month, 1) : WeekStart(anchor);
            return new HistoryInterval { From = Local(first, zone), To = Local(month ? first.AddMonths(1) : first.AddDays(7), zone) };
        }
        public static HistoryInterval Cycle(QuotaWindow window, DateTimeOffset now)
        {
            if (window != null && window.RemainingPercent == 100 && window.CycleResetsAt.HasValue && window.CycleResetsAt <= now && window.DurationMinutes > 0)
                return new HistoryInterval { From = now.AddMinutes(-window.DurationMinutes.Value), To = now };
            var reset = window == null ? null : window.CycleResetsAt > now ? window.CycleResetsAt : window.ResetsAt;
            if (window != null && reset.HasValue && reset > now && window.DurationMinutes > 0)
            {
                var first = reset.Value.AddMinutes(-window.DurationMinutes.Value);
                if (first <= now) return new HistoryInterval { From = first, To = reset.Value };
            }
            return new HistoryInterval { From = window != null && window.DurationMinutes > 0 ? now.AddMinutes(-window.DurationMinutes.Value) : now.AddDays(-7), To = now };
        }
    }
    public sealed class TokenBucket
    {
        public DateTimeOffset From { get; set; }
        public DateTimeOffset To { get; set; }
        public decimal Tokens { get; set; }
        public int ObservedDays { get; set; }
    }
    public static class TokenSeries
    {
        public static DateTimeOffset Date(string day)
        { return new DateTimeOffset(DateTime.ParseExact(day, "yyyy-MM-dd", CultureInfo.InvariantCulture), TimeSpan.Zero); }
        public static List<TokenBucket> Group(IEnumerable<DailyTokens> daily, string grouping)
        {
            // Aggregate calendar buckets, retaining missing dates as missing rather than zeros.
            return daily.GroupBy(d => d.Date, StringComparer.Ordinal).Select(g => g.Last()).GroupBy(d => {
                var date = Date(d.Date).UtcDateTime;
                return grouping == "Month" ? new DateTime(date.Year, date.Month, 1) : grouping == "Week" ? HistoryCalendar.WeekStart(date) : date;
            }).OrderBy(g => g.Key).Select(g => new TokenBucket {
                From = new DateTimeOffset(g.Key, TimeSpan.Zero),
                To = new DateTimeOffset(grouping == "Month" ? g.Key.AddMonths(1) : g.Key.AddDays(grouping == "Week" ? 7 : 1), TimeSpan.Zero),
                Tokens = g.Sum(d => (decimal)d.Tokens), ObservedDays = g.Count()
            }).ToList();
        }
    }
    public sealed class QuotaAnalysis
    {
        public double Consumed { get; set; }
        public bool IsLowerBound { get; set; }
        public DateTimeOffset? ExhaustsAt { get; set; }
        public int Samples { get; set; }
    }
    public static class HistoryAnalytics
    {
        public static List<QuotaSample> Normalize(IEnumerable<QuotaSample> source)
        {
            var output = new List<QuotaSample>();
            var lastByWindow = new Dictionary<string, QuotaSample>();
            foreach (var point in source.OrderBy(p => p.At))
            {
                var copy = new QuotaSample { At = point.At, WindowId = point.WindowId, Title = point.Title, Remaining = point.Remaining,
                    ResetsAt = point.ResetsAt, DurationMinutes = point.DurationMinutes };
                QuotaSample previous;
                lastByWindow.TryGetValue(copy.WindowId, out previous);
                if (previous != null)
                    copy.ResetsAt = QuotaCycle.NormalizeReset(previous.At, previous.Remaining, previous.ResetsAt, previous.DurationMinutes,
                        copy.At, copy.Remaining, copy.ResetsAt, copy.DurationMinutes, previous.WindowId == copy.WindowId);
                output.Add(copy);
                lastByWindow[copy.WindowId] = copy;
            }
            return output;
        }
        public static List<List<QuotaSample>> Cycles(IEnumerable<QuotaSample> source)
        {
            var cycles = new List<List<QuotaSample>>();
            foreach (var point in Normalize(source))
            {
                var cycle = cycles.LastOrDefault(); var previous = cycle == null ? null : cycle.Last();
                if (previous == null || previous.WindowId != point.WindowId || previous.DurationMinutes != point.DurationMinutes ||
                    previous.ResetsAt != point.ResetsAt || point.Remaining > previous.Remaining)
                    cycles.Add(cycle = new List<QuotaSample>());
                cycle.Add(point);
            }
            return cycles;
        }
        public static QuotaAnalysis Analyze(IEnumerable<QuotaSample> source, DateTimeOffset from, DateTimeOffset to, DateTimeOffset now)
        {
            var points = Normalize(source.Where(p => p.At >= from && p.At <= to));
            var result = new QuotaAnalysis { Samples = points.Count, IsLowerBound = points.Count == 0 };
            if (points.Count == 0) return result;
            if (points.Select(p => p.WindowId).Distinct().Count() != 1) throw new ArgumentException("Analyze one quota window at a time.");
            var cycles = Cycles(points); QuotaSample prior = null;
            foreach (var cycle in cycles)
            {
                var first = cycle[0];
                DateTimeOffset? boundary = first.ResetsAt.HasValue && first.DurationMinutes > 0 ?
                    (DateTimeOffset?)first.ResetsAt.Value.AddMinutes(-first.DurationMinutes.Value) : null;
                bool knownStart = boundary.HasValue && boundary >= from && boundary <= first.At && first.At <= first.ResetsAt &&
                    (prior == null || boundary >= prior.At);
                if (knownStart) result.Consumed += Math.Max(0, 100 - first.Remaining);
                else if (prior == null || first.Remaining < 100) result.IsLowerBound = true;
                if (knownStart && first.At - boundary.Value > TimeSpan.FromMinutes(30)) result.IsLowerBound = true;
                for (int i = 1; i < cycle.Count; i++)
                {
                    result.Consumed += Math.Max(0, cycle[i - 1].Remaining - cycle[i].Remaining);
                    if (cycle[i].At - cycle[i - 1].At > TimeSpan.FromMinutes(30)) result.IsLowerBound = true;
                }
                if (prior != null && first.At - prior.At > TimeSpan.FromMinutes(30)) result.IsLowerBound = true;
                prior = cycle.Last();
            }
            var effectiveEnd = to < now ? to : now;
            if (effectiveEnd - points.Last().At > TimeSpan.FromMinutes(15)) result.IsLowerBound = true;
            var latest = HistorySeries.Segments(points).Last();
            var last = latest.Last(); var firstLatest = latest.First();
            if (latest.Count < 3 || latest.Select(p => p.At).Distinct().Count() < 3 || latest.Any(p => !p.ResetsAt.HasValue) ||
                (last.At - firstLatest.At).TotalMinutes < 15 || firstLatest.Remaining - last.Remaining < 2 || last.Remaining <= 0 ||
                now - last.At > TimeSpan.FromMinutes(15) || last.At > now || last.ResetsAt <= now) return result;
            double meanX = latest.Average(p => (p.At - firstLatest.At).TotalMinutes), meanY = latest.Average(p => p.Remaining);
            double denominator = latest.Sum(p => Math.Pow((p.At - firstLatest.At).TotalMinutes - meanX, 2));
            if (denominator <= 0) return result;
            double slope = latest.Sum(p => ((p.At - firstLatest.At).TotalMinutes - meanX) * (p.Remaining - meanY)) / denominator;
            if (slope >= 0) return result;
            double minutes = last.Remaining / -slope;
            if (double.IsNaN(minutes) || double.IsInfinity(minutes) || minutes > (last.ResetsAt.Value - last.At).TotalMinutes) return result;
            var estimate = last.At.AddMinutes(minutes);
            if (estimate > now && estimate < last.ResetsAt.Value) result.ExhaustsAt = estimate;
            return result;
        }
        public static double[] Smooth(IList<QuotaSample> points)
        {
            var values = points.Select(p => p.Remaining).ToArray();
            int[] weights = { 1, 4, 6, 4, 1 };
            for (int i = 1; i < points.Count - 1; i++)
            {
                double sum = 0, weight = 0;
                for (int j = -2; j <= 2; j++)
                    if (i + j >= 0 && i + j < points.Count) { sum += points[i + j].Remaining * weights[j + 2]; weight += weights[j + 2]; }
                values[i] = Math.Min(values[i - 1], Math.Max(points.Last().Remaining, sum / weight));
            }
            return values;
        }
        // Monotone Hermite tangents, in percentage per minute, for display-only Bezier curves.
        public static double[] Tangents(IList<QuotaSample> points, double[] values)
        {
            var slope = new double[Math.Max(0, points.Count - 1)]; var tangent = new double[points.Count];
            for (int i = 0; i < slope.Length; i++)
                slope[i] = (values[i + 1] - values[i]) / Math.Max(.000001, (points[i + 1].At - points[i].At).TotalMinutes);
            if (slope.Length == 0) return tangent;
            tangent[0] = slope[0]; tangent[tangent.Length - 1] = slope.Last();
            for (int i = 1; i < tangent.Length - 1; i++)
                tangent[i] = slope[i - 1] * slope[i] <= 0 ? 0 : 2 * slope[i - 1] * slope[i] / (slope[i - 1] + slope[i]);
            for (int i = 0; i < slope.Length; i++)
            {
                if (slope[i] == 0) { tangent[i] = 0; tangent[i + 1] = 0; continue; }
                double a = tangent[i] / slope[i], b = tangent[i + 1] / slope[i];
                if (a * a + b * b > 9)
                {
                    double scale = 3 / Math.Sqrt(a * a + b * b);
                    tangent[i] = scale * a * slope[i]; tangent[i + 1] = scale * b * slope[i];
                }
            }
            return tangent;
        }
    }
}
