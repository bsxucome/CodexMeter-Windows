using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;

[assembly: DefaultDllImportSearchPaths(DllImportSearchPath.System32)]

namespace CodexMeter.Core
{
    // Windows inbox SQLite, shared by the Framework and modern .NET builds.
    // The owner serializes access. All values are bound, never interpolated into SQL.
    internal sealed class Sqlite : IDisposable
    {
        private IntPtr database;
        public Sqlite(string path, bool readOnly = false)
        {
            int code = sqlite3_open_v2(Utf8(path), out database, (readOnly ? 1 : 2 | 4) | 0x10000, IntPtr.Zero);
            if (code != 0) { Dispose(); throw new IOException("本地历史数据库无法打开。"); }
            sqlite3_busy_timeout(database, 2000);
        }
        private static byte[] Utf8(string value) { return Encoding.UTF8.GetBytes(value + "\0"); }
        public List<object[]> Query(string sql, params object[] values)
        {
            if (database == IntPtr.Zero) throw new ObjectDisposedException("Sqlite");
            IntPtr statement;
            if (sqlite3_prepare_v2(database, Utf8(sql), -1, out statement, IntPtr.Zero) != 0)
                throw new IOException("本地历史数据库查询失败。");
            try
            {
                for (int i = 0; i < values.Length; i++)
                {
                    object value = values[i]; int result;
                    if (value == null) result = sqlite3_bind_null(statement, i + 1);
                    else if (value is string)
                    {
                        var bytes = Utf8((string)value);
                        result = sqlite3_bind_text(statement, i + 1, bytes, bytes.Length - 1, new IntPtr(-1));
                    }
                    else if (value is double || value is float)
                        result = sqlite3_bind_double(statement, i + 1, Convert.ToDouble(value, CultureInfo.InvariantCulture));
                    else result = sqlite3_bind_int64(statement, i + 1, Convert.ToInt64(value, CultureInfo.InvariantCulture));
                    if (result != 0) throw new IOException("本地历史数据绑定失败。");
                }
                var rows = new List<object[]>(); int code;
                while ((code = sqlite3_step(statement)) == 100)
                {
                    var row = new object[sqlite3_column_count(statement)];
                    for (int i = 0; i < row.Length; i++)
                    {
                        int kind = sqlite3_column_type(statement, i);
                        if (kind == 1) row[i] = sqlite3_column_int64(statement, i);
                        else if (kind == 2) row[i] = sqlite3_column_double(statement, i);
                        else if (kind == 3)
                        {
                            int length = sqlite3_column_bytes(statement, i);
                            var bytes = new byte[length];
                            Marshal.Copy(sqlite3_column_text(statement, i), bytes, 0, length);
                            row[i] = Encoding.UTF8.GetString(bytes);
                        }
                    }
                    rows.Add(row);
                }
                if (code != 101) throw new IOException("本地历史数据库读写失败。");
                return rows;
            }
            finally { sqlite3_finalize(statement); }
        }
        public void Transaction(Action action)
        {
            Query("BEGIN IMMEDIATE");
            try { action(); Query("COMMIT"); }
            catch { Query("ROLLBACK"); throw; }
        }
        public void BackupTo(string path)
        {
            using (var destination = new Sqlite(path))
            {
                var backup = sqlite3_backup_init(destination.database, Utf8("main"), database, Utf8("main"));
                if (backup == IntPtr.Zero) throw new IOException("无法创建一致的历史快照。");
                try
                {
                    int result = 5;
                    for (int attempt = 0; attempt < 100; attempt++)
                    {
                        result = sqlite3_backup_step(backup, -1);
                        if (result != 5 && result != 6) break;
                        System.Threading.Thread.Sleep(10);
                    }
                    if (result != 101) throw new IOException("历史快照创建失败。");
                }
                finally { sqlite3_backup_finish(backup); }
            }
        }
        public void Dispose()
        {
            if (database == IntPtr.Zero) return;
            sqlite3_close_v2(database); database = IntPtr.Zero;
        }
        [DllImport("winsqlite3.dll", CallingConvention = CallingConvention.Cdecl)] private static extern int sqlite3_open_v2(byte[] path, out IntPtr db, int flags, IntPtr vfs);
        [DllImport("winsqlite3.dll", CallingConvention = CallingConvention.Cdecl)] private static extern int sqlite3_close_v2(IntPtr db);
        [DllImport("winsqlite3.dll", CallingConvention = CallingConvention.Cdecl)] private static extern int sqlite3_busy_timeout(IntPtr db, int ms);
        [DllImport("winsqlite3.dll", CallingConvention = CallingConvention.Cdecl)] private static extern int sqlite3_prepare_v2(IntPtr db, byte[] sql, int length, out IntPtr statement, IntPtr tail);
        [DllImport("winsqlite3.dll", CallingConvention = CallingConvention.Cdecl)] private static extern int sqlite3_bind_null(IntPtr statement, int index);
        [DllImport("winsqlite3.dll", CallingConvention = CallingConvention.Cdecl)] private static extern int sqlite3_bind_int64(IntPtr statement, int index, long value);
        [DllImport("winsqlite3.dll", CallingConvention = CallingConvention.Cdecl)] private static extern int sqlite3_bind_double(IntPtr statement, int index, double value);
        [DllImport("winsqlite3.dll", CallingConvention = CallingConvention.Cdecl)] private static extern int sqlite3_bind_text(IntPtr statement, int index, byte[] value, int length, IntPtr destructor);
        [DllImport("winsqlite3.dll", CallingConvention = CallingConvention.Cdecl)] private static extern int sqlite3_step(IntPtr statement);
        [DllImport("winsqlite3.dll", CallingConvention = CallingConvention.Cdecl)] private static extern int sqlite3_finalize(IntPtr statement);
        [DllImport("winsqlite3.dll", CallingConvention = CallingConvention.Cdecl)] private static extern int sqlite3_column_count(IntPtr statement);
        [DllImport("winsqlite3.dll", CallingConvention = CallingConvention.Cdecl)] private static extern int sqlite3_column_type(IntPtr statement, int column);
        [DllImport("winsqlite3.dll", CallingConvention = CallingConvention.Cdecl)] private static extern long sqlite3_column_int64(IntPtr statement, int column);
        [DllImport("winsqlite3.dll", CallingConvention = CallingConvention.Cdecl)] private static extern IntPtr sqlite3_backup_init(IntPtr destination, byte[] destinationName, IntPtr source, byte[] sourceName);
        [DllImport("winsqlite3.dll", CallingConvention = CallingConvention.Cdecl)] private static extern int sqlite3_backup_step(IntPtr backup, int pages);
        [DllImport("winsqlite3.dll", CallingConvention = CallingConvention.Cdecl)] private static extern int sqlite3_backup_finish(IntPtr backup);
        [DllImport("winsqlite3.dll", CallingConvention = CallingConvention.Cdecl)] private static extern double sqlite3_column_double(IntPtr statement, int column);
        [DllImport("winsqlite3.dll", CallingConvention = CallingConvention.Cdecl)] private static extern IntPtr sqlite3_column_text(IntPtr statement, int column);
        [DllImport("winsqlite3.dll", CallingConvention = CallingConvention.Cdecl)] private static extern int sqlite3_column_bytes(IntPtr statement, int column);
    }
}
