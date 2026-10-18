using System;
using System.IO;

namespace CodexMeter.Core
{
    // A new sibling directory keeps the running installation available for rollback.
    public static class UpdateInstall
    {
        public static bool Run(SignedRelease release, string archive, string destination, string oldExecutable,
            Func<string, bool> launchAndConfirm, Action<string> restartPrevious)
        {
            ValidateDestination(destination, oldExecutable);
            string target = Path.GetFullPath(destination);
            try
            {
                release.Extract(archive, target);
                if (launchAndConfirm(Path.Combine(target, "CodexMeter.exe"))) return true;
            }
            catch (Exception ex) { if (ex is OutOfMemoryException) throw; }
            restartPrevious(oldExecutable);
            return false;
        }
        public static void ValidateDestination(string destination, string oldExecutable)
        {
            string oldDirectory = Path.GetDirectoryName(Path.GetFullPath(oldExecutable));
            string parent = Path.GetDirectoryName(oldDirectory);
            string target = Path.GetFullPath(destination);
            if (parent == null || !string.Equals(Path.GetDirectoryName(target), parent, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(target, oldDirectory, StringComparison.OrdinalIgnoreCase) || Directory.Exists(target))
                throw new InvalidDataException("更新目录必须是尚未存在的同级目录。");
        }
    }
}
