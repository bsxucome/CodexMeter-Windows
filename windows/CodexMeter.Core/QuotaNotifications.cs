using System;
using System.Collections.Generic;
using System.Linq;

namespace CodexMeter.Core
{
    public sealed class QuotaNotice
    {
        public string SessionId, WindowId, WindowTitle, Kind;
        public double Remaining;
        public DateTimeOffset ObservedAt;
    }
    public sealed class QuotaNotifications
    {
        private sealed class Previous
        {
            public bool Low, Pace, PendingLow, PendingPace;
            public DateTimeOffset LastLow = DateTimeOffset.MinValue, LastPace = DateTimeOffset.MinValue;
            public DateTimeOffset At = DateTimeOffset.MinValue;
            public DateTimeOffset? Reset;
            public double Remaining;
            public double? Duration;
        }
        private readonly Dictionary<string, Previous> windows = new Dictionary<string, Previous>();
        private string session, policy;
        public IList<QuotaNotice> Evaluate(UsageState state, Settings settings, DateTimeOffset now)
        {
            var notices = new List<QuotaNotice>();
            string key = settings.Notifications + "|" + settings.NotifyLowQuota + "|" + settings.NotifyOverPace + "|" + settings.NotificationThreshold;
            if (session != state.SessionId || policy != key) { windows.Clear(); session = state.SessionId; policy = key; }
            if (!settings.Notifications || state.IsStale || state.IsLoading || !state.UpdatedAt.HasValue ||
                state.UpdatedAt > now || now - state.UpdatedAt.Value > TimeSpan.FromMinutes(2)) return notices;
            var active = new HashSet<string>(state.Windows.Select(w => w.Id));
            foreach (var missing in windows.Keys.Where(id => !active.Contains(id)).ToArray()) windows.Remove(missing);
            foreach (var window in state.Windows)
            {
                Previous previous;
                if (!windows.TryGetValue(window.Id, out previous)) windows[window.Id] = previous = new Previous();
                if (state.UpdatedAt <= previous.At) continue;
                var normalizedReset = QuotaCycle.NormalizeReset(previous.At, previous.Remaining, previous.Reset, previous.Duration,
                    state.UpdatedAt.Value, window.RemainingPercent, window.ResetsAt, window.DurationMinutes);
                if (previous.Reset.HasValue && normalizedReset.HasValue && previous.Reset != normalizedReset)
                    previous.Low = previous.Pace = previous.PendingLow = previous.PendingPace = false;
                previous.Reset = normalizedReset; previous.Remaining = window.RemainingPercent; previous.Duration = window.DurationMinutes;
                if (window.ResetsAt.HasValue && window.ResetsAt <= state.UpdatedAt)
                {
                    previous.Low = previous.Pace = previous.PendingLow = previous.PendingPace = false;
                    previous.At = state.UpdatedAt.Value; continue;
                }
                bool low = window.RemainingPercent <= Math.Max(1, Math.Min(99, settings.NotificationThreshold));
                var time = window.TimeRemainingPercent(state.UpdatedAt.Value);
                bool pace = time.HasValue && window.ResetsAt > state.UpdatedAt && window.RemainingPercent < time.Value;
                previous.PendingLow = low && (previous.PendingLow || !previous.Low);
                previous.PendingPace = pace && (previous.PendingPace || !previous.Pace);
                var cooldown = TimeSpan.FromMinutes(Math.Max(1, Math.Min(1440, settings.NotificationCooldownMinutes)));
                bool sendLow = settings.NotifyLowQuota && previous.PendingLow && now - previous.LastLow >= cooldown;
                bool sendPace = settings.NotifyOverPace && previous.PendingPace && now - previous.LastPace >= cooldown;
                if (sendLow || sendPace)
                {
                    notices.Add(new QuotaNotice { SessionId = state.SessionId, WindowId = window.Id, WindowTitle = window.Title,
                        Kind = sendLow ? "Low" : "Pace", Remaining = window.RemainingPercent, ObservedAt = state.UpdatedAt.Value });
                    if (sendLow) { previous.LastLow = now; previous.PendingLow = false; }
                    if (sendPace) { previous.LastPace = now; previous.PendingPace = false; }
                }
                previous.Low = low; previous.Pace = pace; previous.At = state.UpdatedAt.Value;
            }
            return notices;
        }
        public static bool StillApplies(QuotaNotice notice, UsageState state, Settings settings, DateTimeOffset now)
        {
            if (!settings.Notifications || notice.SessionId != state.SessionId || state.IsStale || state.IsLoading ||
                now - notice.ObservedAt > TimeSpan.FromMinutes(2)) return false;
            foreach (var window in state.Windows)
                if (window.Id == notice.WindowId)
                    return (!window.ResetsAt.HasValue || window.ResetsAt > now) && (notice.Kind == "Low" ? settings.NotifyLowQuota && window.RemainingPercent <= settings.NotificationThreshold :
                        settings.NotifyOverPace && window.TimeRemainingPercent(now).HasValue && window.ResetsAt > now &&
                        window.RemainingPercent < window.TimeRemainingPercent(now).Value);
            return false;
        }
    }
}
