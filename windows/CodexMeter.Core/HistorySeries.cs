using System;
using System.Collections.Generic;
using System.Linq;

namespace CodexMeter.Core
{
    public static class HistorySeries
    {
        // Display reduction retains extrema and segment endpoints; raw history stays unchanged.
        public static List<QuotaSample> DisplayPoints(IList<QuotaSample> samples, DateTimeOffset from, DateTimeOffset to, int columns)
        {
            columns = Math.Max(1, Math.Min(4096, columns));
            double span = Math.Max(1, (to - from).TotalSeconds);
            var result = new List<QuotaSample>();
            foreach (var segment in Segments(samples))
                foreach (var bin in segment.GroupBy(p => Math.Max(0, Math.Min(columns - 1, (int)((p.At - from).TotalSeconds / span * columns)))))
                {
                    var points = bin.ToList();
                    result.AddRange(new[] { points[0], points.OrderBy(p => p.Remaining).First(), points.OrderByDescending(p => p.Remaining).First(), points[points.Count - 1] }.Distinct().OrderBy(p => p.At));
                }
            return result;
        }
        public static QuotaSample Nearest(IList<QuotaSample> sorted, DateTimeOffset at)
        {
            if (sorted.Count == 0) return null;
            int low = 0, high = sorted.Count - 1;
            while (low < high) { int middle = low + (high - low) / 2; if (sorted[middle].At < at) low = middle + 1; else high = middle; }
            return low > 0 && (at - sorted[low - 1].At).Duration() <= (sorted[low].At - at).Duration() ? sorted[low - 1] : sorted[low];
        }
        public static List<List<QuotaSample>> Segments(IEnumerable<QuotaSample> samples)
        {
            var segments = new List<List<QuotaSample>>();
            foreach (var sample in samples.OrderBy(s => s.At))
            {
                var current = segments.LastOrDefault();
                var previous = current == null ? null : current.Last();
                if (previous == null || sample.WindowId != previous.WindowId || sample.ResetsAt != previous.ResetsAt ||
                    sample.DurationMinutes != previous.DurationMinutes || sample.Remaining > previous.Remaining ||
                    sample.At - previous.At > TimeSpan.FromMinutes(30))
                    segments.Add(current = new List<QuotaSample>());
                current.Add(sample);
            }
            return segments;
        }
    }
}
