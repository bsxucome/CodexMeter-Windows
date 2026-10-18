using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace CodexMeter.Core
{
    public static class PortableFiles
    {
        public static string Sha256(string path)
        { using (var stream = File.OpenRead(path)) using (var hash = SHA256.Create()) return Hex(hash.ComputeHash(stream)); }
        public static string Hex(byte[] bytes) { return BitConverter.ToString(bytes).Replace("-", "").ToLowerInvariant(); }
        public static void AtomicWrite(string path, byte[] content)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path)));
            string temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                using (var file = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                { file.Write(content, 0, content.Length); file.Flush(true); }
                if (File.Exists(path)) File.Replace(temporary, path, null); else File.Move(temporary, path);
            }
            finally { if (File.Exists(temporary)) File.Delete(temporary); }
        }
        public static byte[] ReadBounded(Stream input, int maximum)
        {
            using (var memory = new MemoryStream()) { CopyBounded(input, memory, maximum); return memory.ToArray(); }
        }
        public static void CopyBounded(Stream input, Stream output, long maximum)
        {
            byte[] buffer = new byte[65536]; int count; long total = 0;
            while ((count = input.Read(buffer, 0, buffer.Length)) > 0)
            { total += count; if (total > maximum) throw new InvalidDataException("文件超过允许大小。"); output.Write(buffer, 0, count); }
        }
        public static string SafeRelativeName(string name)
        {
            if (string.IsNullOrEmpty(name) || name.Length > 240 || name.IndexOf('\\') >= 0 || name.IndexOf(':') >= 0 || name.StartsWith("/"))
                throw new InvalidDataException("压缩包路径无效。");
            foreach (var segment in name.Split('/'))
                if (segment.Length == 0 || segment == "." || segment == ".." || segment.EndsWith(".") || segment.EndsWith(" ") ||
                    segment.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 ||
                    System.Text.RegularExpressions.Regex.IsMatch(segment, @"^(CON|PRN|AUX|NUL|COM[1-9]|LPT[1-9])(\.|$)", System.Text.RegularExpressions.RegexOptions.IgnoreCase))
                    throw new InvalidDataException("压缩包路径无效。");
            return name;
        }
        public static string ChildPath(string parent, string name)
        {
            string root = Path.GetFullPath(parent).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            string path = Path.GetFullPath(Path.Combine(root, SafeRelativeName(name).Replace('/', Path.DirectorySeparatorChar)));
            if (!path.StartsWith(root, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("文件路径超出目标目录。");
            return path;
        }
    }
}
