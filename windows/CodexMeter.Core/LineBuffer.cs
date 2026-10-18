using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace CodexMeter.Core
{
    public sealed class LineBuffer : IDisposable
    {
        public const int MaximumLineBytes = 1024 * 1024;
        private readonly MemoryStream pending = new MemoryStream();
        private readonly UTF8Encoding utf8 = new UTF8Encoding(false, true);
        public IEnumerable<string> Append(byte[] bytes, int count)
        {
            var lines = new List<string>();
            for (int i = 0; i < count; i++)
            {
                if (bytes[i] == 10)
                {
                    var line = utf8.GetString(pending.GetBuffer(), 0, (int)pending.Length).TrimEnd('\r');
                    pending.SetLength(0);
                    if (line.Length > 0) lines.Add(line);
                }
                else
                {
                    if (pending.Length >= MaximumLineBytes) throw new InvalidDataException("App Server 输出超过安全上限，请刷新重试。");
                    pending.WriteByte(bytes[i]);
                }
            }
            return lines;
        }
        public void Dispose() { pending.Dispose(); }
    }
}
