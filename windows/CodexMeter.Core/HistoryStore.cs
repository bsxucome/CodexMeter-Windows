using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace CodexMeter.Core
{
    public sealed class QuotaSample
    {
        public DateTimeOffset At { get; set; }
        public string WindowId { get; set; }
        public string Title { get; set; }
        public double Remaining { get; set; }
        public double? DurationMinutes { get; set; }
        public DateTimeOffset? ResetsAt { get; set; }
    }
    public sealed class HistoryStore : IDisposable
    {
        private readonly object sync = new object();
        private Sqlite db;
        private byte[] salt;
        private static readonly DateTimeOffset Epoch = new DateTimeOffset(1970, 1, 1, 0, 0, 0, TimeSpan.Zero);
        private int retentionDays;
        public string FilePath { get; private set; }
        public static HistoryStore Default() { return new HistoryStore(Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "CodexMeter", "history.sqlite3")); }
        public HistoryStore(string path)
        {
            FilePath = path; Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path)));
            HistoryRestoreJournal.Recover(path);
            db = new Sqlite(path);
            try
            {
                long version = (long)db.Query("PRAGMA user_version")[0][0];
                if (version > 1) throw new IOException("历史数据库来自更新的版本，请更新应用后重试。");
                db.Query("PRAGMA journal_mode=WAL");
                db.Transaction(() => {
                    db.Query("CREATE TABLE IF NOT EXISTS metadata (key TEXT PRIMARY KEY, value TEXT NOT NULL)");
                    db.Query("CREATE TABLE IF NOT EXISTS quota (account TEXT NOT NULL, at INTEGER NOT NULL, window TEXT NOT NULL, title TEXT NOT NULL, remaining REAL NOT NULL, duration REAL, resets INTEGER, PRIMARY KEY(account, window, at))");
                    db.Query("CREATE INDEX IF NOT EXISTS quota_time ON quota(at)");
                    db.Query("CREATE TABLE IF NOT EXISTS tokens (account TEXT NOT NULL, date TEXT NOT NULL, tokens INTEGER NOT NULL, PRIMARY KEY(account,date))");
                    db.Query("PRAGMA user_version=1");
                    var value = new byte[32]; using (var random = RandomNumberGenerator.Create()) random.GetBytes(value);
                    db.Query("INSERT OR IGNORE INTO metadata VALUES('identity_salt',?)", Convert.ToBase64String(value));
                });
                salt = Convert.FromBase64String((string)db.Query("SELECT value FROM metadata WHERE key='identity_salt'")[0][0]);
                if (salt.Length != 32) throw new IOException("历史账号标识无法读取。");
            }
            catch (Exception ex)
            { db.Dispose(); if (ex is FormatException || ex is InvalidCastException || ex is ArgumentException) throw new IOException("历史账号标识无法读取。"); throw; }
        }
        public string AccountKey(string type, string email, string anonymousSession)
        {
            if (string.IsNullOrWhiteSpace(email) && string.IsNullOrEmpty(anonymousSession)) throw new ArgumentException("Anonymous history requires a session identifier.");
            string source = string.IsNullOrWhiteSpace(email) ? "session:" + anonymousSession : "email:" + email.Trim().ToLowerInvariant();
            using (var hash = new HMACSHA256(salt))
                return Convert.ToBase64String(hash.ComputeHash(Encoding.UTF8.GetBytes(type + "|" + source)));
        }
        public static int ValidRetention(int days) { return days == 7 || days == 30 || days == 90 || days == 365 ? days : 0; }
        public void SetRetention(int days)
        {
            lock (sync) { retentionDays = ValidRetention(days); db.Transaction(() => Prune(DateTimeOffset.UtcNow)); }
        }
        private static long Millis(DateTimeOffset value) { return (long)(value - Epoch).TotalMilliseconds; }
        private static DateTimeOffset Time(object value) { return Epoch.AddMilliseconds((long)value); }
        private static double? Number(object value) { return value == null ? null : (double?)Convert.ToDouble(value, CultureInfo.InvariantCulture); }
        private static DateTimeOffset? Reset(object value) { return value == null ? null : (DateTimeOffset?)Time(value); }
        public void Record(string account, DateTimeOffset at, IList<QuotaWindow> windows, TokenUsage usage)
        {
            if (string.IsNullOrEmpty(account)) return;
            lock (sync)
                db.Transaction(() => {
                    foreach (var window in windows)
                    {
                        DateTimeOffset? normalizedReset = window.ResetsAt;
                        var previous = db.Query("SELECT at,remaining,duration,resets,title FROM quota WHERE account=? AND window=? ORDER BY at DESC LIMIT 1", account, window.Id);
                        if (previous.Count > 0)
                        {
                            var row = previous[0];
                            if (at < Time(row[0])) continue;
                            normalizedReset = QuotaCycle.NormalizeReset(Time(row[0]), Number(row[1]).Value, Reset(row[3]), Number(row[2]),
                                at, window.RemainingPercent, window.ResetsAt, window.DurationMinutes);
                            if ((at - Time(row[0])).TotalMinutes < 15 && Number(row[1]) == window.RemainingPercent &&
                                Number(row[2]) == window.DurationMinutes &&
                                (row[3] == null ? (long?)null : (long)row[3]) ==
                                    (normalizedReset.HasValue ? (long?)Millis(normalizedReset.Value) : null) && (string)row[4] == window.Title) continue;
                        }
                        db.Query("INSERT OR REPLACE INTO quota VALUES(?,?,?,?,?,?,?)", account, Millis(at), window.Id, window.Title,
                            window.RemainingPercent, window.DurationMinutes, normalizedReset.HasValue ? (object)Millis(normalizedReset.Value) : null);
                    }
                    if (usage != null && usage.DailyAvailable)
                        foreach (var day in usage.Daily)
                            db.Query("INSERT OR REPLACE INTO tokens SELECT ?,?,? WHERE NOT EXISTS(SELECT 1 FROM tokens WHERE account=? AND date=? AND tokens=?)", account, day.Date, day.Tokens, account, day.Date, day.Tokens);
                    Prune(at);
                });
        }
        private void Prune(DateTimeOffset now)
        {
            if (retentionDays == 0) return;
            db.Query("DELETE FROM quota WHERE at < ?", Millis(now.AddDays(-retentionDays)));
            db.Query("DELETE FROM tokens WHERE date < ?", now.UtcDateTime.Date.AddDays(-retentionDays).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
        }
        public List<QuotaSample> ReadQuota(string account, DateTimeOffset from, DateTimeOffset to, string windowId = null)
        {
            lock (sync)
                return (windowId == null ? db.Query("SELECT at,window,title,remaining,duration,resets FROM quota WHERE account=? AND at>=? AND at<=? ORDER BY at", account, Millis(from), Millis(to)) :
                    db.Query("SELECT at,window,title,remaining,duration,resets FROM quota WHERE account=? AND window=? AND at>=? AND at<=? ORDER BY at", account, windowId, Millis(from), Millis(to)))
                    .Select(row => new QuotaSample { At = Time(row[0]), WindowId = (string)row[1], Title = (string)row[2],
                        Remaining = Number(row[3]).Value, DurationMinutes = Number(row[4]), ResetsAt = Reset(row[5]) }).ToList();
        }
        public List<QuotaSample> Windows(string account)
        {
            lock (sync) return db.Query("SELECT q.window,q.title FROM quota q JOIN (SELECT window,MAX(at) AS at FROM quota WHERE account=? GROUP BY window) m ON q.window=m.window AND q.at=m.at WHERE q.account=? ORDER BY q.duration", account, account)
                .Select(row => new QuotaSample { WindowId = (string)row[0], Title = (string)row[1] }).ToList();
        }
        public QuotaSample LatestQuota(string account, string windowId)
        {
            lock (sync) return db.Query("SELECT at,remaining,duration,resets FROM quota WHERE account=? AND window=? ORDER BY at DESC LIMIT 1", account, windowId)
                .Select(row => new QuotaSample { At = Time(row[0]), WindowId = windowId, Remaining = Number(row[1]).Value,
                    DurationMinutes = Number(row[2]), ResetsAt = Reset(row[3]) }).FirstOrDefault();
        }
        public DateTimeOffset? EarliestQuota(string account, string windowId)
        {
            lock (sync)
            {
                var value = db.Query("SELECT MIN(at) FROM quota WHERE account=? AND window=?", account, windowId)[0][0];
                return value == null ? null : (DateTimeOffset?)Time(value);
            }
        }
        public List<DailyTokens> ReadTokens(string account, string from, string to)
        {
            lock (sync)
                return db.Query("SELECT date,tokens FROM tokens WHERE account=? AND date>=? AND date<=? ORDER BY date", account, from, to)
                    .Select(row => new DailyTokens { Date = (string)row[0], Tokens = (long)row[1] }).ToList();
        }
        public long SizeBytes
        {
            get { lock (sync) return new[] { FilePath, FilePath + "-wal", FilePath + "-shm" }
                .Where(File.Exists).Sum(file => new FileInfo(file).Length); }
        }
        public long Revision { get { lock (sync) return (long)db.Query("SELECT total_changes()")[0][0]; } }
        public void Clear(string account)
        {
            lock (sync) db.Transaction(() => {
                db.Query("DELETE FROM quota WHERE account=?", account); db.Query("DELETE FROM tokens WHERE account=?", account);
            });
        }
        public void ClearAll()
        { lock (sync) db.Transaction(() => { db.Query("DELETE FROM quota"); db.Query("DELETE FROM tokens"); }); }
        public IList<HistoryAccountSummary> Accounts()
        {
            lock (sync) return db.Query("SELECT account,(SELECT COUNT(*) FROM quota q WHERE q.account=a.account),(SELECT COUNT(*) FROM tokens t WHERE t.account=a.account) FROM (SELECT account FROM quota UNION SELECT account FROM tokens) a ORDER BY account")
                .Select(row => new HistoryAccountSummary { Key = (string)row[0], QuotaRows = (long)row[1], TokenRows = (long)row[2] }).ToList();
        }
        public void Snapshot(string path) { lock (sync) db.BackupTo(path); }
        public void RestoreSnapshot(string snapshot, string rollbackArchive, Action afterReplace = null)
        {
            lock (sync)
            {
                HistoryBackup.ValidateDatabase(snapshot);
                HistoryBackup.Create(this, rollbackArchive);
                string candidate = FilePath + ".restore-" + Guid.NewGuid().ToString("N");
                string rollback = FilePath + ".rollback-" + Guid.NewGuid().ToString("N");
                File.Copy(snapshot, candidate, false);
                using (var durable = new FileStream(candidate, FileMode.Open, FileAccess.Write, FileShare.None)) durable.Flush(true);
                bool replaced = false;
                db.Dispose(); db = null;
                try
                {
                    // Refuse replacement if another SQLite owner still has journal state or the main file open.
                    if (File.Exists(FilePath + "-wal") || File.Exists(FilePath + "-shm")) throw new IOException("历史数据库仍被其他程序占用。");
                    using (var exclusive = new FileStream(FilePath, FileMode.Open, FileAccess.ReadWrite, FileShare.None)) { }
                    HistoryRestoreJournal.Prepare(FilePath, rollback, candidate);
                    File.Replace(candidate, FilePath, rollback); replaced = true;
                    if (afterReplace != null) afterReplace();
                    HistoryBackup.ValidateDatabase(FilePath);
                    db = new Sqlite(FilePath); salt = ReadSalt(db);
                    HistoryRestoreJournal.Commit(FilePath);
                }
                catch
                {
                    if (db != null) { db.Dispose(); db = null; }
                    if (replaced) File.Replace(rollback, FilePath, null);
                    HistoryRestoreJournal.Recover(FilePath);
                    db = new Sqlite(FilePath); salt = ReadSalt(db);
                    throw;
                }
                finally { if (File.Exists(candidate)) File.Delete(candidate); if (db != null) HistoryRestoreJournal.CleanupCommitted(FilePath); }
            }
        }
        private static byte[] ReadSalt(Sqlite source)
        { return Convert.FromBase64String((string)source.Query("SELECT value FROM metadata WHERE key='identity_salt'")[0][0]); }
        private static string Csv(string value)
        {
            // Prevent spreadsheet formula execution for service-supplied labels.
            if (!string.IsNullOrEmpty(value) && "=+-@\t\r".IndexOf(value[0]) >= 0) value = "'" + value;
            return "\"" + (value ?? "").Replace("\"", "\"\"") + "\"";
        }
        public void ExportCsv(string account, string path, DateTimeOffset from, DateTimeOffset to, string tokenFrom = null, string tokenTo = null)
        {
            // No account identifier, email, or authentication data enters exports.
            var quota = ReadQuota(account, from, to);
            var tokens = ReadTokens(account, tokenFrom ?? from.UtcDateTime.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), tokenTo ?? to.UtcDateTime.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
            using (var writer = new StreamWriter(path, false, new UTF8Encoding(true)))
            {
                writer.WriteLine("kind,observed_at_utc,window_id,title,remaining_percent,duration_minutes,resets_at_utc,service_date,tokens,observed_unix_seconds,observed_at_local");
                foreach (var sample in quota)
                    writer.WriteLine("quota," + Csv(sample.At.ToString("O")) + "," + Csv(sample.WindowId) + "," + Csv(sample.Title) + "," +
                        sample.Remaining.ToString("R", CultureInfo.InvariantCulture) + "," +
                        (sample.DurationMinutes.HasValue ? sample.DurationMinutes.Value.ToString("R", CultureInfo.InvariantCulture) : "") + "," +
                        Csv(sample.ResetsAt.HasValue ? sample.ResetsAt.Value.ToString("O") : "") + ",,," +
                        ((long)(sample.At - Epoch).TotalSeconds).ToString(CultureInfo.InvariantCulture) + "," + Csv(sample.At.ToLocalTime().ToString("O")));
                foreach (var day in tokens) writer.WriteLine("tokens,,,,,,," + Csv(day.Date) + "," + day.Tokens.ToString(CultureInfo.InvariantCulture) + ",,");
            }
        }
        public void Dispose() { lock (sync) { if (db != null) db.Dispose(); } }
    }
    public sealed class HistoryAccountSummary { public string Key; public long QuotaRows, TokenRows; }
}
