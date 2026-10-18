using System;
using System.Collections.Generic;
using System.Linq;

namespace CodexMeter.Core
{
    public struct OccupiedSpan
    {
        public double Start, End;
        public OccupiedSpan(double start, double end) { Start = start; End = end; }
    }
    public static class TaskbarLayout
    {
        public static double? FindStartSlot(double start, double end, double length, IList<OccupiedSpan> occupied,
            OccupiedSpan startButton, bool before, double gap)
        {
            double candidate = before ? startButton.Start - gap - length : startButton.End + gap;
            if (length <= 0 || gap < 0 || candidate < start || candidate + length > end) return null;
            if (occupied.Any(s => s.End > candidate && s.Start < candidate + length)) return null;
            return candidate;
        }
        // Work only with axis positions; DPI conversion and Explorer probing stay in the desktop layer.
        public static double? FindSlot(double start, double end, double length, IList<OccupiedSpan> occupied, double preference)
        {
            const double padding = 6;
            if (length <= 0 || end - start < length + padding * 2) return null;
            var spans = occupied.Where(s => s.End > start && s.Start < end && s.End > s.Start)
                .OrderBy(s => s.Start).ToList();
            var candidates = new List<double>();
            double cursor = start + padding;
            double preferred = preference < 0 ? end : start + Math.Max(0, Math.Min(1, preference)) * (end - start);
            foreach (var span in spans)
            {
                double gapEnd = Math.Min(end - padding, span.Start - padding);
                if (gapEnd - cursor >= length) candidates.Add(Math.Max(cursor, Math.Min(preferred - length / 2, gapEnd - length)));
                cursor = Math.Max(cursor, span.End + padding);
            }
            if (end - padding - cursor >= length)
                candidates.Add(Math.Max(cursor, Math.Min(preferred - length / 2, end - padding - length)));
            if (candidates.Count == 0) return null;
            return candidates.OrderBy(c => Math.Abs(c + length / 2 - preferred)).First();
        }
    }
}
