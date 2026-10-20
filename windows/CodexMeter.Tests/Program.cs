using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using CodexMeter.Core;

namespace CodexMeter.Tests
{
    internal static partial class Program
    {
        private static int passed;
        private static int Main(string[] args)
        {
            if (args.Contains("--mock")) return MockServer.Run(args);
            try
            {
                if (args.Length == 2 && args[0] == "--transport-probe") { Diagnostics.TransportProbe(args[1]).GetAwaiter().GetResult(); return 0; }
                if (args.Length == 2 && args[0] == "--probe") { Probe(args[1]).GetAwaiter().GetResult(); return 0; }
                if (args.Length == 2 && args[0] == "--release-script-fixture") { ReleaseScriptFixture(args[1]); return 0; }
                if (args.Length == 2 && args[0] == "--interrupt-restore") { InterruptRestore(args[1]); return 0; }
                if (args.Length == 1 && args[0] == "--transport-tests") { Transport().GetAwaiter().GetResult(); Console.WriteLine("PASS: " + passed + " transport scenarios"); return 0; }
                if (args.Length == 1 && args[0] == "--locator-tests") { Locator(); Console.WriteLine("PASS: " + passed + " locator scenarios"); return 0; }
                if (args.Length == 6 && args[0] == "--verify-release")
                { SignedRelease.Verify(File.ReadAllText(args[1]), File.ReadAllText(args[2]), args[4], args[5]).VerifyArchive(args[3]); Console.WriteLine("PASS external signed release fixture"); return 0; }
                Run().GetAwaiter().GetResult();
                Console.WriteLine("PASS: " + passed + " scenarios");
                return 0;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine("FAIL: " + ex.GetType().Name + ": " + ex.Message);
                Console.Error.WriteLine(ex.StackTrace);
                return 1;
            }
        }
        private static void Check(bool condition, string name)
        { if (!condition) throw new Exception(name); passed++; Console.WriteLine("PASS " + name); }
        private static async Task Run()
        {
            Quotas(); Framing(); JsonFailures(); SettingsRoundtrip(); Locator(); Quotes(); TaskbarPlacement(); IndicatorClicks();
            Tokens(); History(); Analytics(); Appearance(); SystemFeatures();
            BackupFeatures(); UpdateFeatures(); await RemoteFeatures(); await UpdateTransportFeatures(); await RestoreServiceFeatures();
            FinalParityFeatures(); await InterruptedRestoreFeatures();
            await Transport(); await Service(); await HistoryService(); await AllowancesService();
        }
        private static void Quotas()
        {
            var now = DateTimeOffset.UtcNow;
            var window = new QuotaWindow { UsedPercent = 35, DurationMinutes = 100, ResetsAt = now.AddMinutes(50) };
            Check(window.RemainingPercent == 65 && window.TimeRemainingPercent(now) == 50, "quota and time share a scale");
            Check(window.State(now) == Attention.Normal, "normal pacing");
            window.UsedPercent = 65;
            Check(window.State(now) == Attention.Warning, "over-pace detection");
            window.UsedPercent = 81;
            Check(window.State(now) == Attention.Critical, "critical overrides pacing");
            window.UsedPercent = -10; Check(window.RemainingPercent == 100, "negative usage clamps");
            window.UsedPercent = 120; Check(window.RemainingPercent == 0, "overfull usage clamps");
            window.UsedPercent = 40; window.ResetsAt = null;
            Check(!window.TimeRemainingPercent(now).HasValue && window.State(now) == Attention.Normal, "missing reset does not invent pacing");
            window.ResetsAt = now.AddHours(-1); Check(window.TimeRemainingPercent(now) == 0, "expired reset clamps time");
            var old = QuotaParser.Parse(Json.Parse("{\"rateLimits\":{\"primary\":{\"usedPercent\":25,\"windowDurationMins\":300}}}"));
            Check(old.Count == 1 && old[0].Id == "codex:300", "legacy quota format");
            var newer = QuotaParser.Parse(Json.Parse(MockServer.Limits));
            Check(newer.Count == 3 && newer[0].DurationMinutes == 300, "multi-bucket quota format and ordering");
            Check(QuotaParser.Select(newer, "").Id == "codex:10080", "default standard weekly quota");
            Check(QuotaParser.Select(newer, "other:10080").Id == "other:10080", "explicit alternate quota");
            Check(QuotaParser.Select(newer, "absent").Id == "codex:10080", "missing selection falls back");
            Check(QuotaParser.Parse(Json.Parse("{\"rateLimits\":{\"primary\":null,\"secondary\":{\"usedPercent\":\"wrong\"}}}")).Count == 0,
                "nullable and malformed windows");
            var swap = QuotaParser.Parse(Json.Parse("{\"rateLimits\":{\"secondary\":{\"usedPercent\":30,\"windowDurationMins\":300}}}"));
            Check(old[0].Id == swap[0].Id, "slot swap preserves window identity");
            var invalid = QuotaParser.Parse(Json.Parse("{\"rateLimits\":{\"primary\":{\"usedPercent\":10,\"windowDurationMins\":-1,\"resetsAt\":1e30}}}"));
            Check(!invalid[0].DurationMinutes.HasValue && !invalid[0].ResetsAt.HasValue, "invalid reset and duration degrade safely");
        }
        private static void Framing()
        {
            using (var buffer = new LineBuffer())
            {
                var bytes = Encoding.UTF8.GetBytes("{\"text\":\"中文\"}\r\n{}\n");
                var lines = new List<string>();
                for (int i = 0; i < bytes.Length; i++) lines.AddRange(buffer.Append(new[] { bytes[i] }, 1));
                Check(lines.Count == 2 && Json.Text(Json.Parse(lines[0]), "text") == "中文", "split UTF-8 and coalesced CRLF messages");
            }
            using (var buffer = new LineBuffer())
            {
                bool rejected = false;
                try { buffer.Append(Enumerable.Repeat((byte)'x', LineBuffer.MaximumLineBytes + 1).ToArray(), LineBuffer.MaximumLineBytes + 1); }
                catch (InvalidDataException) { rejected = true; }
                Check(rejected, "oversized output bounded at 1 MiB");
            }
        }
        private static void JsonFailures()
        {
            foreach (var input in new[] { "{broken PRIVATE_JSON_SENTINEL", new string('[', 70) + "0" + new string(']', 70) })
            {
                bool rejected = false;
                try { Json.Parse(input); }
                catch (FormatException ex) { rejected = ex.InnerException == null && !ex.ToString().Contains("PRIVATE_JSON_SENTINEL"); }
                Check(rejected, "JSON syntax/depth errors use a safe runtime-independent exception");
            }
        }
        private static void SettingsRoundtrip()
        {
            var directory = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "test-data", "CodexMeter-test-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);
            try
            {
                var store = new SettingsStore(Path.Combine(directory, "settings.json")); string warning;
                Check(store.Load(out warning).Theme == "System" && warning == null, "fresh defaults");
                store.Save(new Settings { CliPath = @"C:\含空格 path\codex.exe", SelectedWindow = "codex:300", Theme = "Dark" });
                var settings = store.Load(out warning);
                Check(settings.Theme == "Dark" && settings.SelectedWindow == "codex:300" && settings.CliPath.Contains("含空格"), "settings UTF-8 roundtrip");
                settings.Theme = "Light"; store.Save(settings);
                Check(store.Load(out warning).Theme == "Light", "atomic settings replacement");
                settings.TaskbarIndicator = false; settings.TaskbarPosition = .65; store.Save(settings);
                var restored = store.Load(out warning);
                Check(!restored.TaskbarIndicator && restored.TaskbarPosition == .65, "taskbar visibility and position persist");
                Check(restored.Copy().TaskbarPosition == .65 && !restored.Copy().TaskbarIndicator, "settings copy preserves taskbar preferences");
                File.WriteAllText(store.FilePath, "{\"theme\":\"Dark\"}");
                restored = store.Load(out warning);
                Check(restored.TaskbarIndicator && restored.TaskbarPosition == -1, "existing settings enable taskbar with automatic placement");
                File.WriteAllText(store.FilePath, "{\"taskbarPosition\":0.25}");
                restored = store.Load(out warning);
                Check(restored.TaskbarDock == "StartLeft", "legacy left position migrates to Start anchoring");
                restored.TaskbarDock = "Custom"; store.Save(restored);
                Check(store.Load(out warning).TaskbarDock == "Custom", "explicit custom docking survives restart");
                restored.TaskbarDock = "StartRight"; store.Save(restored);
                Check(store.Load(out warning).Copy().TaskbarDock == "StartRight", "settings copies and persists Start side");
                File.WriteAllText(store.FilePath, "{broken");
                Check(store.Load(out warning).Theme == "System" && warning != null, "corrupt settings recover");
                File.WriteAllText(store.FilePath, "null");
                Check(store.Load(out warning).Theme == "System", "null settings recover");
            }
            finally { DeleteTestDirectory(directory); }
        }
        private static void Locator()
        {
            // Preserve all GUID bits in 22 filesystem-safe characters. The npm
            // tree must fit legacy Framework limits in long hosted checkout paths.
            string id = Convert.ToBase64String(Guid.NewGuid().ToByteArray()).TrimEnd('=').Replace('+', '-').Replace('/', '_');
            var directory = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "test-data", "中 " + id);
            var package = Path.Combine(directory, "node_modules", "@openai", "codex");
            var native = Path.Combine(package, "node_modules", "@openai", "codex-win32-x64", "vendor", "x86_64-pc-windows-msvc", "bin", "codex.exe");
            try
            {
                Check(Path.GetDirectoryName(native).Length < 248 && native.Length < 260 && directory.Contains("中 "),
                    "npm fixture retains Unicode and spaces within legacy path limits");
                Directory.CreateDirectory(Path.GetDirectoryName(native)); Directory.CreateDirectory(Path.Combine(package, "bin"));
                File.WriteAllText(native, "fixture"); File.WriteAllText(Path.Combine(package, "bin", "codex.js"), "fixture");
                var shim = Path.Combine(directory, "codex.cmd"); File.WriteAllText(shim, "fixture");
                var locator = new CliLocator();
                Check(locator.Locate(shim).Executable == native, "npm shim resolves to native executable");
                Check(locator.Locate(native).Executable == native, "explicit native path with spaces");
                bool rejected = false;
                try { locator.Locate(Path.Combine(directory, "missing.exe")); } catch (InvalidOperationException) { rejected = true; }
                Check(rejected, "invalid custom path does not silently switch accounts");
                File.Delete(native);
                var nodePath = Path.Combine(directory, "node.exe");
                File.WriteAllText(nodePath, "fixture");
                var fallback = locator.Locate(shim);
                Check(fallback.Executable == nodePath && fallback.PrefixArguments.Length == 1 &&
                    fallback.PrefixArguments[0] == Path.Combine(package, "bin", "codex.js"), "npm fallback resolves Node without NODE_EXE");
            }
            finally { if (Directory.Exists(directory)) DeleteTestDirectory(directory); }
        }
        private static void DeleteTestDirectory(string directory)
        {
            var allowedRoot = Path.GetFullPath(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "test-data")) + Path.DirectorySeparatorChar;
            var resolved = Path.GetFullPath(directory);
            if (!resolved.StartsWith(allowedRoot, StringComparison.OrdinalIgnoreCase)) throw new IOException("Unsafe test cleanup target.");
            Directory.Delete(resolved, true);
        }
        private static void TaskbarPlacement()
        {
            var occupied = new List<OccupiedSpan> { new OccupiedSpan(0, 180), new OccupiedSpan(400, 700), new OccupiedSpan(1000, 1200) };
            var right = TaskbarLayout.FindSlot(0, 1200, 104, occupied, -1);
            Check(right.HasValue && right >= 706 && right + 104 <= 994, "taskbar auto placement avoids occupied controls");
            var left = TaskbarLayout.FindSlot(0, 1200, 104, occupied, .22);
            Check(left.HasValue && left >= 186 && left + 104 <= 394, "drag preference chooses nearest safe gap");
            Check(!TaskbarLayout.FindSlot(0, 100, 104, new List<OccupiedSpan>(), -1).HasValue, "narrow taskbar has no unsafe slot");
            Check(!TaskbarLayout.FindSlot(0, 1200, 104, new List<OccupiedSpan> { new OccupiedSpan(0, 1200) }, -1).HasValue,
                "fully occupied taskbar requests fallback");
            var merged = TaskbarLayout.FindSlot(0, 600, 100, new List<OccupiedSpan> { new OccupiedSpan(200, 400), new OccupiedSpan(100, 300) }, -1);
            Check(merged.HasValue && merged >= 406 && merged + 100 <= 594, "overlapping taskbar controls merge safely");
            var scaled = TaskbarLayout.FindSlot(0, 1800, 156, occupied.Select(s => new OccupiedSpan(s.Start * 1.5, s.End * 1.5)).ToList(), -1);
            Check(scaled.HasValue && scaled >= 1056 && scaled + 156 <= 1494, "taskbar placement supports scaled physical pixels");
            var start = new OccupiedSpan(600, 648);
            var near = new List<OccupiedSpan> { new OccupiedSpan(0, 180), start, new OccupiedSpan(648, 900) };
            var anchored = TaskbarLayout.FindStartSlot(0, 1200, 76, near, start, true, 4);
            Check(anchored == 520 && start.Start - (anchored.Value + 76) == 4, "left docking stays four pixels from Start");
            var scaledStart = new OccupiedSpan(900, 972);
            var scaledLeft = TaskbarLayout.FindStartSlot(0, 1800, 114, new List<OccupiedSpan> { scaledStart }, scaledStart, true, 6);
            Check(scaledLeft == 780 && scaledStart.Start - (scaledLeft.Value + 114) == 6, "Start gap stays four DIPs at 150 percent scale");
            Check(!TaskbarLayout.FindStartSlot(0, 1200, 76, near, start, false, 4).HasValue, "Start docking never covers adjacent app buttons");
            Check(!TaskbarLayout.FindStartSlot(0, 1200, 76, new List<OccupiedSpan> { new OccupiedSpan(0, 48) },
                new OccupiedSpan(0, 48), true, 4).HasValue, "left-aligned Start requests a close fallback instead of a distant gap");
            var rightStart = TaskbarLayout.FindStartSlot(0, 1200, 76, new List<OccupiedSpan> { start }, start, false, 4);
            Check(rightStart == 652, "right docking anchors to the Start edge");
            Check(!TaskbarLayout.FindStartSlot(0, 1200, 76, new List<OccupiedSpan> { new OccupiedSpan(500, 560), start },
                start, true, 4).HasValue, "blocked Start gap does not jump to unrelated empty space");
        }
        private static void Quotes()
        {
            Check(LaunchCommand.Quote(@"a b\") == "\"a b\\\\\"", "trailing slash command quoting");
            Check(LaunchCommand.Quote("a\"b") == "\"a\\\"b\"", "embedded quote command quoting");
        }
        internal static LaunchCommand Mock(string mode)
        {
            var assembly = Assembly.GetExecutingAssembly().Location;
            if (assembly.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) return new LaunchCommand(assembly, "--mock", mode);
            var host = Process.GetCurrentProcess().MainModule.FileName;
            return Path.GetFileNameWithoutExtension(host).Equals("dotnet", StringComparison.OrdinalIgnoreCase) ?
                new LaunchCommand(host, assembly, "--mock", mode) : new LaunchCommand(host, "--mock", mode);
        }
        private static async Task Transport()
        {
            var previousEncoding = Console.InputEncoding;
            try
            {
                Console.InputEncoding = new UTF8Encoding(true);
                using (var connection = new RpcConnection(Mock("fragment")))
                using (var cancellation = new CancellationTokenSource(5000))
                {
                    Check(Console.InputEncoding.CodePage == 65001 && Console.InputEncoding.GetPreamble().Length == 3,
                        "RPC launch restores the parent's exact UTF-8 console encoding");
                    var result = await connection.RequestAsync("echo", new { value = "中文额度 🚀" }, cancellation.Token);
                    Check(Json.Text(result, "value") == "中文额度 🚀", "BOM-free first JSON request and fragmented Unicode response work under a UTF-8 console");
                }
            }
            finally { Console.InputEncoding = previousEncoding; }
            using (var connection = new RpcConnection(Mock("fragment")))
            using (var cancellation = new CancellationTokenSource(5000))
            {
                var response = await connection.RequestAsync("account/read", new { }, cancellation.Token);
                Check(Json.Object(response, "account") != null, "real child pipe supports fragmented replies");
            }
            using (var connection = new RpcConnection(Mock("reorder")))
            using (var cancellation = new CancellationTokenSource(5000))
            {
                var first = connection.RequestAsync("first", new { }, cancellation.Token);
                var second = connection.RequestAsync("second", new { }, cancellation.Token);
                await Task.WhenAll(first, second);
                Check(Json.Text(first.Result, "method") == "first" && Json.Text(second.Result, "method") == "second",
                    "out-of-order responses match request IDs");
            }
            using (var connection = new RpcConnection(Mock("stderr")))
            using (var cancellation = new CancellationTokenSource(5000))
            {
                await connection.RequestAsync("account/read", new { }, cancellation.Token);
                Check(connection.DiagnosticByteCount <= 8192, "large stderr drained with bounded diagnostics");
            }
            using (var connection = new RpcConnection(Mock("tree")))
            using (var cancellation = new CancellationTokenSource(5000))
            {
                var result = await connection.RequestAsync("tree", new { }, cancellation.Token);
                int descendant = (int)Json.Number(result, "pid").Value;
                connection.Dispose();
                await Task.Delay(300);
                bool exited;
                try { exited = Process.GetProcessById(descendant).HasExited; } catch (ArgumentException) { exited = true; }
                Check(exited, "job disposal terminates descendant processes");
            }
            using (var connection = new RpcConnection(Mock("rpc-error")))
            using (var cancellation = new CancellationTokenSource(5000))
            {
                bool sanitized = false;
                try { await connection.RequestAsync("account/read", new { }, cancellation.Token); }
                catch (RpcException ex) { sanitized = ex.Code == -32603 && !ex.Message.Contains("PRIVATE_SENTINEL"); }
                Check(sanitized, "raw RPC errors never reach user-facing exceptions");
            }
            foreach (var mode in new[] { "exit", "oversize" })
            {
                using (var connection = new RpcConnection(Mock(mode)))
                using (var cancellation = new CancellationTokenSource(5000))
                {
                    bool failed = false;
                    try { await connection.RequestAsync("account/read", new { }, cancellation.Token); }
                    catch (IOException) { failed = true; }
                    Check(failed, mode + " fails pending requests");
                }
            }
            using (var connection = new RpcConnection(Mock("hang")))
            using (var cancellation = new CancellationTokenSource(100))
            {
                bool timedOut = false;
                try { await connection.RequestAsync("account/read", new { }, cancellation.Token); }
                catch (OperationCanceledException) { timedOut = true; }
                Check(timedOut, "cancellation times out a silent child");
                int pid = connection.ProcessId;
                connection.Dispose();
                await Task.WhenAny(connection.Completion, Task.Delay(3000));
                bool exited;
                try { exited = Process.GetProcessById(pid).HasExited; } catch (ArgumentException) { exited = true; }
                Check(exited && connection.Completion.IsCompleted, "dispose terminates child and drains pipe tasks");
            }
        }
        private static async Task Service()
        {
            var fake = new FakeConnection();
            var factory = new FakeFactory(fake);
            using (var service = new UsageService(new FixedLocator(Mock("normal")), factory))
            {
                await service.RefreshAsync(true);
                Check(service.State.Windows.Count == 3 && !service.State.IsStale, "service initializes and reads live quota");
                Check(fake.Methods[0] == "initialize" && fake.Methods[1] == "initialized" && fake.Methods[2] == "account/read",
                    "initialization precedes account queries");
                fake.FailLimits = true;
                await service.RefreshAsync(true);
                Check(service.State.IsStale && service.State.Windows.Count == 3 && service.State.Error != null,
                    "refresh failure retains stale quota");
                Check(service.State.NextRefresh > DateTimeOffset.UtcNow.AddSeconds(8) &&
                    service.State.NextRefresh <= DateTimeOffset.UtcNow.AddSeconds(11), "first failure schedules bounded backoff");
                fake = new FakeConnection { LoggedIn = false }; factory.Connection = fake;
                await service.RefreshAsync(true);
                Check(service.State.Windows.Count == 0 && !service.State.UpdatedAt.HasValue, "logout clears previous quota");
            }
            fake = new FakeConnection(); factory = new FakeFactory(fake);
            using (var service = new UsageService(new FixedLocator(Mock("normal")), factory))
            {
                await service.RefreshAsync(true);
                fake.DelayLimits = true;
                var refresh = service.RefreshAsync(true);
                await fake.LimitsEntered.Task;
                var overlap = service.RefreshAsync(true);
                await overlap;
                Check(fake.PendingLimits == 1, "overlapping refreshes are merged");
                fake.Emit("account/updated");
                Check(service.State.Windows.Count == 0, "account boundary clears display immediately");
                fake.LoggedIn = false;
                fake.DelayedLimits.TrySetResult(Json.Parse(MockServer.Limits));
                await refresh;
                await Task.Delay(100);
                Check(service.State.Windows.Count == 0, "late quota cannot repopulate old account");
            }
            fake = new FakeConnection { DelayLimits = true }; factory = new FakeFactory(fake);
            using (var service = new UsageService(new FixedLocator(Mock("normal")), factory))
            {
                service.RequestTimeoutMilliseconds = 100;
                await service.RefreshAsync(true);
                Check(service.State.Error != null && !service.State.IsLoading, "service timeout releases refresh state");
            }
        }
        private static async Task Probe(string path)
        {
            using (var service = new UsageService(new CliLocator(), new RpcFactory()))
            {
                service.Configure(path);
                await service.RefreshAsync(true);
                var state = service.State;
                Console.WriteLine("Probe: windows=" + state.Windows.Count + ", stale=" + state.IsStale +
                    ", error=" + (state.Error ?? "none"));
                if (state.Windows.Count == 0) throw new Exception("Real account quota was not verified.");
            }
        }
        private sealed class FixedLocator : ICliLocator
        {
            private readonly LaunchCommand command;
            public FixedLocator(LaunchCommand command) { this.command = command; }
            public LaunchCommand Locate(string ignored) { return command; }
        }
        private sealed class FakeFactory : IRpcFactory
        {
            public FakeConnection Connection;
            public FakeFactory(FakeConnection connection) { Connection = connection; }
            public IRpcConnection Start(LaunchCommand command) { Connection.Alive = true; return Connection; }
        }
        private sealed class FakeConnection : IRpcConnection
        {
            public bool Alive = true, LoggedIn = true, FailLimits, DelayLimits, FailUsage, DelayUsage;
            public string Email = "a@example.test";
            public string LimitsJson = MockServer.Limits;
            public string UsageJson = "{\"summary\":{\"lifetimeTokens\":1000},\"dailyUsageBuckets\":[{\"startDate\":\"2026-10-18\",\"tokens\":150}]}";
            public TaskCompletionSource<bool> UsageEntered = new TaskCompletionSource<bool>();
            public TaskCompletionSource<Dictionary<string, object>> DelayedUsage = new TaskCompletionSource<Dictionary<string, object>>();
            public int PendingLimits;
            public List<string> Methods = new List<string>();
            public TaskCompletionSource<bool> LimitsEntered = new TaskCompletionSource<bool>();
            public TaskCompletionSource<Dictionary<string, object>> DelayedLimits = new TaskCompletionSource<Dictionary<string, object>>();
            public event Action<string> Notification;
            public event Action Disconnected { add { } remove { } }
            public bool IsAlive { get { return Alive; } }
            public void Emit(string method) { var handler = Notification; if (handler != null) handler(method); }
            public async Task<Dictionary<string, object>> RequestAsync(string method, object parameters, CancellationToken cancellation)
            {
                Methods.Add(method);
                if (method == "account/read") return Json.Parse(LoggedIn ? "{\"account\":{\"type\":\"chatgpt\",\"planType\":\"plus\",\"email\":\"" + Email + "\"}}" : "{\"account\":null}");
                if (method == "account/usage/read")
                {
                    if (FailUsage) throw new RpcException(-32601);
                    if (DelayUsage)
                    {
                        UsageEntered.TrySetResult(true);
                        using (cancellation.Register(() => DelayedUsage.TrySetCanceled())) return await DelayedUsage.Task;
                    }
                    return Json.Parse(UsageJson);
                }
                if (method != "account/rateLimits/read") return new Dictionary<string, object>();
                if (FailLimits) throw new RpcException(-32603);
                if (DelayLimits)
                {
                    PendingLimits++; LimitsEntered.TrySetResult(true);
                    using (cancellation.Register(() => DelayedLimits.TrySetCanceled()))
                        return await DelayedLimits.Task;
                }
                return Json.Parse(LimitsJson);
            }
            public Task NotifyAsync(string method, object parameters, CancellationToken cancellation)
            { Methods.Add(method); return Task.FromResult(0); }
            public void Dispose() { Alive = false; }
        }
    }
}
