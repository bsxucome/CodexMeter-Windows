using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace CodexMeter.Core
{
    public sealed class CreditBalance
    {
        public bool? HasCredits { get; set; }
        public bool? Unlimited { get; set; }
        public string Balance { get; set; }
        public static CreditBalance Parse(Dictionary<string, object> result)
        {
            var buckets = Json.Object(result, "rateLimitsByLimitId");
            var snapshot = Json.Object(buckets, "codex") ?? Json.Object(result, "rateLimits");
            var data = Json.Object(snapshot, "credits");
            if (data == null) return null;
            decimal number;
            string raw = Json.Text(data, "balance");
            string balance = raw != null && raw.Length <= 64 && decimal.TryParse(raw, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out number) && number >= 0 ? raw : null;
            var has = Json.Get(data, "hasCredits") as bool?; var unlimited = Json.Get(data, "unlimited") as bool?;
            return has.HasValue || unlimited.HasValue || balance != null ? new CreditBalance { HasCredits = has, Unlimited = unlimited, Balance = balance } : null;
        }
    }
    public sealed class ResetCredit
    {
        public string Id { get; set; }
        public string Status { get; set; }
        public string ResetType { get; set; }
        public string Title { get; set; }
        public string Description { get; set; }
        public DateTimeOffset? GrantedAt { get; set; }
        public DateTimeOffset? ExpiresAt { get; set; }
        public bool Usable(DateTimeOffset now)
        { return Status == "available" && ResetType == "codexRateLimits" && (!ExpiresAt.HasValue || ExpiresAt > now); }
        public double? RemainingLifetime(DateTimeOffset now)
        {
            if (!GrantedAt.HasValue || !ExpiresAt.HasValue || ExpiresAt <= GrantedAt) return null;
            return Math.Max(0, Math.Min(100, (ExpiresAt.Value - now).TotalSeconds / (ExpiresAt.Value - GrantedAt.Value).TotalSeconds * 100));
        }
    }
    public sealed class ResetCredits
    {
        public int AvailableCount { get; set; }
        public bool DetailsAvailable { get; set; }
        public IList<ResetCredit> Credits { get; set; }
        public ResetCredits() { Credits = new List<ResetCredit>(); }
        public IList<ResetCredit> UsableCredits(DateTimeOffset now)
        { return Credits.Where(c => c.Usable(now)).OrderBy(c => c.ExpiresAt ?? DateTimeOffset.MaxValue).Take(Math.Max(0, AvailableCount)).ToList(); }
        public static DateTimeOffset? Timestamp(Dictionary<string, object> data, string key)
        {
            double? seconds = Json.Number(data, key);
            if (!seconds.HasValue) return null;
            try { return new DateTimeOffset(1970, 1, 1, 0, 0, 0, TimeSpan.Zero).AddSeconds(seconds.Value); }
            catch (ArgumentOutOfRangeException) { return null; }
        }
        public static ResetCredits Parse(Dictionary<string, object> result)
        {
            var data = Json.Object(result, "rateLimitResetCredits");
            var count = Json.Number(data, "availableCount");
            if (!count.HasValue || count < 0 || count > 1000000 || count != Math.Floor(count.Value)) return null;
            var items = Json.Get(data, "credits") as IEnumerable;
            bool details = items != null && !(items is string) && !(items is IDictionary);
            var output = new ResetCredits { AvailableCount = (int)count, DetailsAvailable = details };
            if (details)
                foreach (var row in items.Cast<object>().Take(256))
                {
                    var item = row as Dictionary<string, object>; var id = Json.Text(item, "id");
                    if (string.IsNullOrEmpty(id) || id.Length > 512 || output.Credits.Any(c => c.Id == id)) continue;
                    output.Credits.Add(new ResetCredit { Id = id, Status = Json.Text(item, "status"), ResetType = Json.Text(item, "resetType"),
                        Title = Bounded(Json.Text(item, "title")), Description = Bounded(Json.Text(item, "description")),
                        GrantedAt = Timestamp(item, "grantedAt"), ExpiresAt = Timestamp(item, "expiresAt") });
                }
            return output;
        }
        private static string Bounded(string value) { return value == null ? null : value.Substring(0, Math.Min(value.Length, 1000)); }
    }
}
