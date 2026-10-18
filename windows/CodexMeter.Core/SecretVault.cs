using System;
using System.IO;
using System.Runtime.InteropServices;

namespace CodexMeter.Core
{
    public interface ISecretVault { byte[] Read(); void Write(byte[] value); }
    public sealed class WindowsSecretVault : ISecretVault
    {
        private readonly string path;
        public WindowsSecretVault(string path) { this.path = path; }
        public byte[] Read()
        {
            if (!File.Exists(path)) return null;
            if (new FileInfo(path).Length > 65536) throw new InvalidDataException("加密凭据无法读取。");
            return Transform(File.ReadAllBytes(path), false);
        }
        public void Write(byte[] value)
        {
            if (value.Length > 32768) throw new InvalidDataException("远程配置超过允许大小。");
            PortableFiles.AtomicWrite(path, Transform(value, true));
        }
        [StructLayout(LayoutKind.Sequential)] private struct Blob { public int Length; public IntPtr Data; }
        private static byte[] Transform(byte[] bytes, bool protect)
        {
            var input = new Blob { Length = bytes.Length, Data = Marshal.AllocHGlobal(Math.Max(1, bytes.Length)) }; var output = new Blob();
            try
            {
                Marshal.Copy(bytes, 0, input.Data, bytes.Length);
                bool success = protect ? CryptProtectData(ref input, null, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, 1, out output) :
                    CryptUnprotectData(ref input, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, 1, out output);
                if (!success) throw new IOException("Windows 用户加密存储无法访问。", new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error()));
                var result = new byte[output.Length]; Marshal.Copy(output.Data, result, 0, result.Length); return result;
            }
            finally
            {
                for (int i = 0; i < bytes.Length; i++) Marshal.WriteByte(input.Data, i, 0);
                Marshal.FreeHGlobal(input.Data);
                if (output.Data != IntPtr.Zero) { for (int i = 0; i < output.Length; i++) Marshal.WriteByte(output.Data, i, 0); LocalFree(output.Data); }
            }
        }
        [DllImport("crypt32.dll", CharSet = CharSet.Unicode, SetLastError = true)] private static extern bool CryptProtectData(ref Blob input, string description, IntPtr entropy, IntPtr reserved, IntPtr prompt, int flags, out Blob output);
        [DllImport("crypt32.dll", SetLastError = true)] private static extern bool CryptUnprotectData(ref Blob input, IntPtr description, IntPtr entropy, IntPtr reserved, IntPtr prompt, int flags, out Blob output);
        [DllImport("kernel32.dll")] private static extern IntPtr LocalFree(IntPtr pointer);
    }
}
