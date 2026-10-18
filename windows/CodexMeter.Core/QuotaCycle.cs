using System;

namespace CodexMeter.Core
{
    public static class QuotaCycle
    {
        public static DateTimeOffset? NormalizeReset(DateTimeOffset previousAt, double previousRemaining, DateTimeOffset? previousReset,
            double? previousDuration, DateTimeOffset at, double remaining, DateTimeOffset? reset, double? duration, bool sameWindow = true)
        {
            if (!sameWindow || previousDuration != duration || !previousReset.HasValue || !reset.HasValue || at < previousAt) return reset;
            double delta = (reset.Value - previousReset.Value).TotalSeconds;
            if (Math.Abs(delta) <= 120 && remaining <= previousRemaining) return previousReset;
            // A moving deadline while an observed window remains unused is not a consumed/reset cycle.
            if (previousRemaining == 100 && remaining == 100 && duration > 0 && reset > at && delta >= 0 &&
                at - previousAt <= TimeSpan.FromMinutes(30) && (reset.Value - at).TotalMinutes <= duration.Value + 2) return previousReset;
            return reset;
        }
    }
}
