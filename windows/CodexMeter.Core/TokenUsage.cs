using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace CodexMeter.Core
{
    public sealed class DailyTokens
    {
        // Service calendar date, retained without inventing a local/UTC conversion.
        public string Date { get; set; }
        public long Tokens { get; set; }
    }
    public sealed class TokenUsage
    {
        public long? LifetimeTokens { get; set; }
        public long? PeakDailyTokens { get; set; }
        public long? LongestRunningTurnSeconds { get; set; }
        public long? CurrentStreakDays { get; set; }
        public long? LongestStreakDays { get; set; }
        public IList<DailyTokens> Daily { get; set; }
        public bool DailyAvailable { get; set; }
        public bool Available { get; set; }
        public TokenUsage() { Daily = new List<DailyTokens>(); }
        private static long? Count(Dictionary<string, object> value, string key)
        {
            double? number = Json.Number(value, key);
            // JSON numbers outside the exact integer range cannot be represented faithfully.
            return number.HasValue && number >= 0 && number <= 9007199254740991 && Math.Floor(number.Value) == number ?
                (long?)number.Value : null;
        }
        public static TokenUsage Parse(Dictionary<string, object> result)
        {
            var summary = Json.Object(result, "summary");
            var parsed = new TokenUsage {
                LifetimeTokens = Count(summary, "lifetimeTokens"), PeakDailyTokens = Count(summary, "peakDailyTokens"),
                LongestRunningTurnSeconds = Count(summary, "longestRunningTurnSec"),
                CurrentStreakDays = Count(summary, "currentStreakDays"), LongestStreakDays = Count(summary, "longestStreakDays")
            };
            var daily = Json.Get(result, "dailyUsageBuckets") as IEnumerable;
            parsed.DailyAvailable = daily != null && !(daily is string) && !(daily is IDictionary);
            var dates = new Dictionary<string, DailyTokens>(StringComparer.Ordinal);
            if (parsed.DailyAvailable)
                foreach (var item in daily)
                {
                    var entry = item as Dictionary<string, object>;
                    string date = Json.Text(entry, "startDate"); DateTime checkedDate;
                    long? tokens = Count(entry, "tokens");
                    if (date != null && DateTime.TryParseExact(date, "yyyy-MM-dd", CultureInfo.InvariantCulture,
                        DateTimeStyles.None, out checkedDate) && tokens.HasValue)
                        dates[date] = new DailyTokens { Date = date, Tokens = tokens.Value };
                }
            parsed.Daily = dates.Values.OrderBy(d => d.Date, StringComparer.Ordinal).ToList();
            parsed.Available = parsed.DailyAvailable || parsed.LifetimeTokens.HasValue || parsed.PeakDailyTokens.HasValue ||
                parsed.LongestRunningTurnSeconds.HasValue || parsed.CurrentStreakDays.HasValue || parsed.LongestStreakDays.HasValue;
            return parsed;
        }
    }
}
