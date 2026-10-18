using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;

namespace CodexMeter.Core
{
    public sealed class HistoryBackupInfo
    {
        public long QuotaRows, TokenRows, Accounts;
        public string CreatedAt;
    }
    public sealed class PreparedHistory : IDisposable
    {
        public string DatabasePath { get; internal set; }
        public HistoryBackupInfo Info { get; internal set; }
        public void Dispose() { if (File.Exists(DatabasePath)) File.Delete(DatabasePath); }
    }
    public static class HistoryBackup
    {
        public const long MaximumDatabaseBytes = 512L * 1024 * 1024;
        public static HistoryBackupInfo ValidateDatabase(string path)
        {
            try { return ValidateCore(path); }
            catch (Exception ex)
            {
                if (ex is ArgumentException || ex is FormatException || ex is InvalidCastException || ex is OverflowException) throw new IOException("历史数据库包含无效内容。");
                throw;
            }
        }
        private static HistoryBackupInfo ValidateCore(string path)
        {
            if (!File.Exists(path) || new FileInfo(path).Length > MaximumDatabaseBytes || new FileInfo(path).Length < 100) throw new InvalidDataException("历史快照大小无效。");
            using (var db = new Sqlite(path, true))
            {
                if ((long)db.Query("PRAGMA user_version")[0][0] != 1 || (string)db.Query("PRAGMA quick_check")[0][0] != "ok") throw new InvalidDataException("历史数据库版本或完整性无效。");
                if (db.Query("SELECT name FROM sqlite_master WHERE type IN ('view','trigger')").Count != 0) throw new InvalidDataException("历史数据库结构无效。");
                var tables = db.Query("SELECT name FROM sqlite_master WHERE type='table' ORDER BY name").Select(r => (string)r[0]).ToArray();
                if (!tables.SequenceEqual(new[] { "metadata", "quota", "tokens" })) throw new InvalidDataException("历史数据库结构无效。");
                CheckColumns(db, "metadata", new[] { "key:TEXT:0:1", "value:TEXT:1:0" });
                CheckColumns(db, "quota", new[] { "account:TEXT:1:1", "at:INTEGER:1:3", "window:TEXT:1:2", "title:TEXT:1:0", "remaining:REAL:1:0", "duration:REAL:0:0", "resets:INTEGER:0:0" });
                CheckColumns(db, "tokens", new[] { "account:TEXT:1:1", "date:TEXT:1:2", "tokens:INTEGER:1:0" });
                var saltRows = db.Query("SELECT value FROM metadata WHERE key='identity_salt'");
                if (saltRows.Count != 1 || db.Query("SELECT key FROM metadata").Count != 1) throw new InvalidDataException("历史身份信息无效。");
                byte[] salt;
                try { salt = Convert.FromBase64String((string)saltRows[0][0]); } catch (FormatException) { throw new InvalidDataException("历史身份信息无效。"); }
                if (salt.Length != 32) throw new InvalidDataException("历史身份信息无效。");
                long badQuota = (long)db.Query("SELECT COUNT(*) FROM quota WHERE typeof(account)!='text' OR length(account)>128 OR typeof(window)!='text' OR length(window)>256 OR typeof(title)!='text' OR length(title)>4096 OR typeof(at)!='integer' OR at<0 OR at>253402300799999 OR typeof(remaining) NOT IN ('real','integer') OR remaining<0 OR remaining>100 OR (duration IS NOT NULL AND (typeof(duration) NOT IN ('real','integer') OR duration<=0 OR duration>52560000)) OR (resets IS NOT NULL AND (typeof(resets)!='integer' OR resets<0 OR resets>253402300799999))")[0][0];
                long badTokens = (long)db.Query("SELECT COUNT(*) FROM tokens WHERE typeof(account)!='text' OR length(account)>128 OR typeof(tokens)!='integer' OR tokens<0 OR typeof(date)!='text' OR length(date)!=10 OR date NOT GLOB '[0-9][0-9][0-9][0-9]-[0-9][0-9]-[0-9][0-9]' OR strftime('%Y-%m-%d',date,'+0 days') IS NULL OR strftime('%Y-%m-%d',date,'+0 days')!=date")[0][0];
                if (badQuota != 0 || badTokens != 0) throw new InvalidDataException("历史记录包含无效数据。");
                return new HistoryBackupInfo { QuotaRows = (long)db.Query("SELECT COUNT(*) FROM quota")[0][0], TokenRows = (long)db.Query("SELECT COUNT(*) FROM tokens")[0][0],
                    Accounts = (long)db.Query("SELECT COUNT(*) FROM (SELECT account FROM quota UNION SELECT account FROM tokens)")[0][0] };
            }
        }
        private static void CheckColumns(Sqlite db, string table, string[] expected)
        { if (!db.Query("PRAGMA table_info(" + table + ")").Select(r => r[1] + ":" + r[2] + ":" + r[3] + ":" + r[5]).SequenceEqual(expected)) throw new InvalidDataException("历史数据库结构无效。"); }
        public static void Create(HistoryStore store, string destination)
        {
            string fullDestination = Path.GetFullPath(destination);
            if (new[] { store.FilePath, store.FilePath + "-wal", store.FilePath + "-shm" }.Any(p => string.Equals(Path.GetFullPath(p), fullDestination, StringComparison.OrdinalIgnoreCase)))
                throw new InvalidDataException("备份不能覆盖正在使用的数据库。");
            string directory = Path.GetDirectoryName(Path.GetFullPath(destination)); Directory.CreateDirectory(directory);
            string database = Path.Combine(directory, ".history-snapshot-" + Guid.NewGuid().ToString("N"));
            string archivePath = Path.Combine(directory, ".history-backup-" + Guid.NewGuid().ToString("N"));
            try
            {
                store.Snapshot(database); var info = ValidateDatabase(database);
                var manifest = new Dictionary<string, object> { { "format", "CodexMeter.History" }, { "version", 1 }, { "schema", 1 },
                    { "createdAt", DateTimeOffset.UtcNow.ToString("O") }, { "sha256", PortableFiles.Sha256(database) }, { "bytes", new FileInfo(database).Length },
                    { "quotaRows", info.QuotaRows }, { "tokenRows", info.TokenRows }, { "accounts", info.Accounts } };
                using (var file = new FileStream(archivePath, FileMode.CreateNew))
                using (var archive = new ZipArchive(file, ZipArchiveMode.Create))
                {
                    using (var output = archive.CreateEntry("history.sqlite3").Open()) using (var input = File.OpenRead(database)) input.CopyTo(output);
                    using (var output = archive.CreateEntry("manifest.json").Open())
                    { var bytes = Encoding.UTF8.GetBytes(Json.Encode(manifest)); output.Write(bytes, 0, bytes.Length); }
                }
                if (File.Exists(destination)) File.Replace(archivePath, destination, null); else File.Move(archivePath, destination);
            }
            finally { if (File.Exists(database)) File.Delete(database); if (File.Exists(archivePath)) File.Delete(archivePath); }
        }
        public static PreparedHistory Prepare(string archivePath, string temporaryDirectory)
        {
            if (new FileInfo(archivePath).Length > MaximumDatabaseBytes + 1048576) throw new InvalidDataException("备份文件过大。");
            Directory.CreateDirectory(temporaryDirectory);
            string database = Path.Combine(temporaryDirectory, ".restore-" + Guid.NewGuid().ToString("N") + ".sqlite3");
            try
            {
                using (var file = File.OpenRead(archivePath)) using (var archive = new ZipArchive(file, ZipArchiveMode.Read))
                {
                    if (archive.Entries.Count != 2 || archive.Entries.Count(e => e.FullName == "history.sqlite3") != 1 || archive.Entries.Count(e => e.FullName == "manifest.json") != 1)
                        throw new InvalidDataException("备份格式无效。");
                    Dictionary<string, object> manifest;
                    using (var input = archive.GetEntry("manifest.json").Open()) manifest = Json.Parse(Encoding.UTF8.GetString(PortableFiles.ReadBounded(input, 16384)));
                    if (Json.Text(manifest, "format") != "CodexMeter.History" || Json.Number(manifest, "version") != 1 || Json.Number(manifest, "schema") != 1)
                        throw new InvalidDataException("备份版本不受支持。");
                    var entry = archive.GetEntry("history.sqlite3");
                    if (entry.Length > MaximumDatabaseBytes || Json.Number(manifest, "bytes") != entry.Length) throw new InvalidDataException("备份大小不一致。");
                    using (var input = entry.Open()) using (var output = new FileStream(database, FileMode.CreateNew)) PortableFiles.CopyBounded(input, output, MaximumDatabaseBytes);
                    if (!string.Equals(PortableFiles.Sha256(database), Json.Text(manifest, "sha256"), StringComparison.Ordinal)) throw new InvalidDataException("备份校验失败。");
                    var info = ValidateDatabase(database);
                    if (Json.Number(manifest, "quotaRows") != info.QuotaRows || Json.Number(manifest, "tokenRows") != info.TokenRows || Json.Number(manifest, "accounts") != info.Accounts)
                        throw new InvalidDataException("备份记录数量不一致。");
                    info.CreatedAt = Json.Text(manifest, "createdAt"); return new PreparedHistory { DatabasePath = database, Info = info };
                }
            }
            catch { if (File.Exists(database)) File.Delete(database); throw; }
        }
    }
}
