using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using CodexMeter.Core;

namespace CodexMeter.Windows
{
    // Explicit fixture commands isolate process integration tests from all production state.
    internal static class UpdateFixture
    {
        internal static string DirectoryPath { get; private set; }
        internal static bool Enabled { get { return DirectoryPath != null; } }
        internal static void Initialize(string directory)
        {
            string full = Path.GetFullPath(directory);
            if (!Regex.IsMatch(Path.GetFileName(full), "^update-process-[a-f0-9]{32}$") ||
                (File.GetAttributes(full) & FileAttributes.ReparsePoint) != 0 ||
                File.ReadAllText(Path.Combine(full, ".fixture"), Encoding.UTF8) != "CodexMeter.UpdateFixture.v1") throw new IOException("Invalid update fixture.");
            DirectoryPath = full;
        }
        internal static Settings Settings()
        { string warning; return new SettingsStore(Path.Combine(DirectoryPath, "settings.json")).Load(out warning); }
        internal static int Install(string stage)
        {
            try
            {
                var settings = Settings(); string manifest = File.ReadAllText(Path.Combine(UpdateWorker.Stage(stage), "manifest.json"), Encoding.UTF8);
                var release = SignedRelease.Verify(manifest, settings.UpdatePublicKey, settings.UpdateChannel, SignedRelease.CurrentRuntime);
                using (var cancel = new CancellationTokenSource(70000)) UpdateWorker.Begin(stage, release, settings.UpdatePublicKey, cancel.Token).GetAwaiter().GetResult();
                return 0;
            }
            catch (Exception ex) { if (ex is OutOfMemoryException) throw; return 1; }
        }
        internal static int Rollback()
        {
            PortableFiles.AtomicWrite(Path.Combine(DirectoryPath, "rollback.json"), Encoding.UTF8.GetBytes(Json.Encode(new { executable = Process.GetCurrentProcess().MainModule.FileName, pid = Process.GetCurrentProcess().Id })));
            return 0;
        }
    }
}
