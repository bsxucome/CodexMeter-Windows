using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;

namespace CodexMeter.Core
{
    public static class HistoryRestoreJournal
    {
        private static string Journal(string database) { return database + ".restore-journal.json"; }
        private static Dictionary<string, object> Read(string database)
        {
            try { return ReadCore(database); }
            catch (Exception ex)
            {
                if (ex is ArgumentException || ex is FormatException || ex.GetType().Name == "JsonException") throw new IOException("历史恢复日志无法校验。");
                throw;
            }
        }
        private static Dictionary<string, object> ReadCore(string database)
        {
            using (var file = File.OpenRead(Journal(database)))
            {
                var data = Json.Parse(Encoding.UTF8.GetString(PortableFiles.ReadBounded(file, 4096)));
                if (Json.Text(data, "format") != "CodexMeter.HistoryRestore" || Json.Number(data, "version") != 1 ||
                    (Json.Text(data, "phase") != "prepared" && Json.Text(data, "phase") != "committed") ||
                    !Regex.IsMatch(Json.Text(data, "oldHash") ?? "", "^[a-f0-9]{64}$")) throw new IOException("历史恢复日志无法校验。");
                return data;
            }
        }
        public static void Prepare(string database, string rollback, string candidate)
        {
            if (File.Exists(Journal(database))) throw new IOException("有尚未完成的历史恢复。");
            var data = new Dictionary<string, object> { { "format", "CodexMeter.HistoryRestore" }, { "version", 1 }, { "phase", "prepared" },
                { "oldHash", PortableFiles.Sha256(database) }, { "rollback", Path.GetFileName(rollback) }, { "candidate", Path.GetFileName(candidate) } };
            Write(database, data);
        }
        private static void Write(string database, Dictionary<string, object> data)
        { PortableFiles.AtomicWrite(Journal(database), Encoding.UTF8.GetBytes(Json.Encode(data))); }
        public static void Commit(string database)
        { var data = Read(database); data["phase"] = "committed"; Write(database, data); }
        private static string OwnedFile(string database, Dictionary<string, object> data, string key, string suffix)
        {
            string name = Json.Text(data, key), prefix = Path.GetFileName(database) + suffix;
            if (name == null || !name.StartsWith(prefix, StringComparison.Ordinal) || !Regex.IsMatch(name.Substring(prefix.Length), "^[a-f0-9]{32}$")) throw new IOException("历史恢复路径无效。");
            return PortableFiles.ChildPath(Path.GetDirectoryName(Path.GetFullPath(database)), name);
        }
        public static void Recover(string database)
        {
            if (!File.Exists(Journal(database))) return;
            var data = Read(database);
            string rollback = OwnedFile(database, data, "rollback", ".rollback-"), candidate = OwnedFile(database, data, "candidate", ".restore-");
            if (Json.Text(data, "phase") == "prepared")
            {
                if (File.Exists(rollback))
                {
                    if (PortableFiles.Sha256(rollback) != Json.Text(data, "oldHash")) throw new IOException("恢复前数据库校验失败；已保留恢复文件。");
                    HistoryBackup.ValidateDatabase(rollback);
                    if (File.Exists(database))
                    {
                        using (var probe = new FileStream(database, FileMode.Open, FileAccess.ReadWrite, FileShare.None)) { }
                        // Let SQLite resolve a crashed owner's journal before replacing the database.
                        using (var pending = new Sqlite(database))
                        { var checkpoint = pending.Query("PRAGMA wal_checkpoint(TRUNCATE)"); if (checkpoint.Count > 0 && (long)checkpoint[0][0] != 0) throw new IOException("历史恢复仍被其他程序占用。"); }
                        if (File.Exists(database + "-wal") || File.Exists(database + "-shm")) throw new IOException("历史恢复仍被其他程序占用。");
                        File.Replace(rollback, database, null);
                    }
                    else File.Move(rollback, database);
                }
                else if (!File.Exists(database) || PortableFiles.Sha256(database) != Json.Text(data, "oldHash")) throw new IOException("历史恢复不完整；请使用恢复前备份。");
            }
            else HistoryBackup.ValidateDatabase(database);
            File.Delete(Journal(database));
            if (File.Exists(rollback)) File.Delete(rollback);
            if (File.Exists(candidate)) File.Delete(candidate);
        }
        public static void CleanupCommitted(string database)
        { try { Recover(database); } catch (IOException) { } catch (UnauthorizedAccessException) { } }
    }
}
