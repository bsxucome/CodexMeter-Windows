using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace CodexMeter.Core
{
    public sealed class LaunchCommand
    {
        public string Executable { get; private set; }
        public string[] PrefixArguments { get; private set; }
        public LaunchCommand(string executable, params string[] prefix)
        { Executable = executable; PrefixArguments = prefix; }
        public string Arguments(params string[] arguments)
        { return string.Join(" ", PrefixArguments.Concat(arguments).Select(Quote)); }
        // Windows command-line quoting, including trailing backslashes before a closing quote.
        public static string Quote(string value)
        {
            var result = new System.Text.StringBuilder("\"");
            int slashes = 0;
            foreach (char c in value)
            {
                if (c == '\\') { slashes++; continue; }
                if (c == '"') { result.Append('\\', slashes * 2 + 1); result.Append(c); slashes = 0; continue; }
                result.Append('\\', slashes); slashes = 0; result.Append(c);
            }
            result.Append('\\', slashes * 2); result.Append('"');
            return result.ToString();
        }
    }
    public interface ICliLocator { LaunchCommand Locate(string configuredPath); }
    public sealed class CliLocator : ICliLocator
    {
        public LaunchCommand Locate(string configuredPath)
        {
            if (!string.IsNullOrWhiteSpace(configuredPath))
            {
                var chosen = Resolve(Environment.ExpandEnvironmentVariables(configuredPath.Trim().Trim('"')));
                if (chosen == null) throw new InvalidOperationException("所选 Codex 路径无效。请选择 codex.exe 或 npm 安装的 codex.cmd。");
                return chosen;
            }
            foreach (var directory in PathDirectories())
            {
                var command = Resolve(Path.Combine(directory, "codex.exe")) ?? Resolve(Path.Combine(directory, "codex.cmd"));
                if (command != null) return command;
            }
            var npm = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "npm");
            var installed = Resolve(Path.Combine(npm, "codex.cmd"));
            if (installed != null) return installed;
            var local = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".local", "bin", "codex.exe");
            installed = Resolve(local);
            if (installed != null) return installed;
            throw new InvalidOperationException("未找到 Codex CLI。请安装并运行 codex login，或在设置中选择 CLI 路径。");
        }
        private static IEnumerable<string> PathDirectories()
        {
            return (Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator)
                .Select(p => p.Trim().Trim('"')).Where(p => p.Length > 0 && Directory.Exists(p)).Distinct(StringComparer.OrdinalIgnoreCase);
        }
        private static LaunchCommand Resolve(string file)
        {
            if (!File.Exists(file)) return null;
            var extension = Path.GetExtension(file);
            if (string.Equals(extension, ".exe", StringComparison.OrdinalIgnoreCase)) return new LaunchCommand(Path.GetFullPath(file));
            if (!(extension.Equals(".cmd", StringComparison.OrdinalIgnoreCase) ||
                  extension.Equals(".ps1", StringComparison.OrdinalIgnoreCase) ||
                  extension.Equals(".js", StringComparison.OrdinalIgnoreCase) || extension == "")) return null;
            var directory = Path.GetDirectoryName(Path.GetFullPath(file));
            string script = string.Equals(extension, ".js", StringComparison.OrdinalIgnoreCase) ? Path.GetFullPath(file) : Path.Combine(directory, "node_modules", "@openai", "codex", "bin", "codex.js");
            if (!File.Exists(script)) return null;
            var packageDirectory = Directory.GetParent(Path.GetDirectoryName(script));
            if (packageDirectory == null) return null;
            var package = packageDirectory.FullName;
            var scopeDirectory = Directory.GetParent(package);
            var platform = Environment.Is64BitOperatingSystem ? "codex-win32-x64" : "";
            if (platform.Length == 0) return null;
            foreach (var root in new[] { Path.Combine(package, "node_modules", "@openai", platform),
                Path.Combine(scopeDirectory == null ? package : scopeDirectory.FullName, platform), package })
            {
                foreach (var suffix in new[] { Path.Combine("vendor", "x86_64-pc-windows-msvc", "bin", "codex.exe"),
                    Path.Combine("vendor", "x86_64-pc-windows-msvc", "codex", "codex.exe") })
                {
                    var native = Path.Combine(root, suffix);
                    if (File.Exists(native)) return new LaunchCommand(native);
                }
            }
            var nodeOverride = Environment.GetEnvironmentVariable("NODE_EXE");
            var nodeDirectory = !string.IsNullOrWhiteSpace(nodeOverride) && File.Exists(nodeOverride) ?
                Path.GetDirectoryName(Path.GetFullPath(nodeOverride)) : null;
            var node = new[] { directory, nodeDirectory }
                .Where(p => !string.IsNullOrWhiteSpace(p)).Concat(PathDirectories())
                .Select(p => Path.Combine(p, "node.exe")).FirstOrDefault(File.Exists);
            return node == null ? null : new LaunchCommand(node, Path.GetFullPath(script));
        }
    }
}
