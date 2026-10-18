using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace CodexMeter.Core
{
    public sealed class RpcException : Exception
    {
        public int Code { get; private set; }
        public RpcException(int code) : base("Codex 未能完成请求，请检查登录状态后重试。") { Code = code; }
    }
    public interface IRpcConnection : IDisposable
    {
        event Action<string> Notification;
        event Action Disconnected;
        bool IsAlive { get; }
        Task<Dictionary<string, object>> RequestAsync(string method, object parameters, CancellationToken cancellation);
        Task NotifyAsync(string method, object parameters, CancellationToken cancellation);
    }
    public interface IRpcFactory { IRpcConnection Start(LaunchCommand command); }
    public sealed class RpcFactory : IRpcFactory
    { public IRpcConnection Start(LaunchCommand command) { return new RpcConnection(command); } }

    public sealed class RpcConnection : IRpcConnection
    {
        private readonly object sync = new object();
        private readonly Dictionary<int, TaskCompletionSource<Dictionary<string, object>>> pending =
            new Dictionary<int, TaskCompletionSource<Dictionary<string, object>>>();
        private readonly SemaphoreSlim writer = new SemaphoreSlim(1, 1);
        private readonly Process process;
        private readonly ChildJob job;
        private int nextId, ended, disposed;
        public event Action<string> Notification;
        public event Action Disconnected;
        public bool IsAlive { get { return Volatile.Read(ref ended) == 0 && Volatile.Read(ref disposed) == 0; } }
        public Task Completion { get; private set; }
        public int ProcessId { get { return process.Id; } }
        public int DiagnosticByteCount { get; private set; }
        public string DiagnosticCategory
        {
            get
            {
                lock (sync)
                {
                    var text = Encoding.UTF8.GetString(diagnosticTail, 0, DiagnosticByteCount);
                    if (text.IndexOf("Could not find home directory", StringComparison.OrdinalIgnoreCase) >= 0) return "home-unavailable";
                    if (text.IndexOf("Access is denied", StringComparison.OrdinalIgnoreCase) >= 0 ||
                        text.IndexOf("Permission denied", StringComparison.OrdinalIgnoreCase) >= 0 ||
                        text.IndexOf("os error 5", StringComparison.OrdinalIgnoreCase) >= 0) return "access-denied";
                    return DiagnosticByteCount == 0 ? "none" : "other";
                }
            }
        }
        private readonly byte[] diagnosticTail = new byte[8192];
#if NETFRAMEWORK
        private static readonly object startEncodingSync = new object();
#endif

        private static void StartProcess(Process child)
        {
#if NETFRAMEWORK
            // Framework creates StandardInput with Console.InputEncoding and
            // immediately flushes its preamble. Under CP65001 that puts a BOM
            // before our first JSON line, even though we write to BaseStream.
            // Keep the console code page unchanged; restore its exact encoding
            // after constructing the pipe's writer with BOM-free UTF-8.
            lock (startEncodingSync)
            {
                var previous = Console.InputEncoding;
                if (previous.CodePage != 65001 || previous.GetPreamble().Length == 0) { child.Start(); return; }
                try { Console.InputEncoding = new UTF8Encoding(false); child.Start(); }
                finally { Console.InputEncoding = previous; }
            }
#else
            child.StartInfo.StandardInputEncoding = new UTF8Encoding(false);
            child.Start();
#endif
        }

        public RpcConnection(LaunchCommand command)
        {
            var start = new ProcessStartInfo {
                FileName = command.Executable, Arguments = command.Arguments("app-server", "--listen", "stdio://"),
                UseShellExecute = false, CreateNoWindow = true,
                RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true,
                WorkingDirectory = Path.GetDirectoryName(command.Executable)
            };
            var executableDirectory = Path.GetDirectoryName(command.Executable);
            start.EnvironmentVariables["PATH"] = executableDirectory + Path.PathSeparator + (start.EnvironmentVariables["PATH"] ?? "");
            process = new Process { StartInfo = start };
            try
            {
                StartProcess(process);
                job = new ChildJob(process);
                Completion = Task.WhenAll(ReadOutputAsync(), DrainErrorsAsync());
            }
            catch
            {
                try { if (!process.HasExited) process.Kill(); } catch (InvalidOperationException) { }
                process.Dispose();
                throw new IOException("无法启动 Codex App Server，请检查 CLI 路径与运行环境。");
            }
        }
        public async Task<Dictionary<string, object>> RequestAsync(string method, object parameters, CancellationToken cancellation)
        {
            var id = Interlocked.Increment(ref nextId);
            var source = new TaskCompletionSource<Dictionary<string, object>>(TaskCreationOptions.RunContinuationsAsynchronously);
            lock (sync)
            {
                if (!IsAlive) throw new IOException("Codex App Server 已停止，请刷新重试。");
                pending[id] = source;
            }
            using (cancellation.Register(() => source.TrySetCanceled()))
            {
                try
                {
                    cancellation.ThrowIfCancellationRequested();
                    await SendAsync(new Dictionary<string, object> { { "id", id }, { "method", method }, { "params", parameters } },
                        cancellation).ConfigureAwait(false);
                    return await source.Task.ConfigureAwait(false);
                }
                finally { lock (sync) pending.Remove(id); }
            }
        }
        public Task NotifyAsync(string method, object parameters, CancellationToken cancellation)
        {
            return SendAsync(new Dictionary<string, object> { { "method", method }, { "params", parameters } }, cancellation);
        }
        private async Task SendAsync(object value, CancellationToken cancellation)
        {
            await writer.WaitAsync(cancellation).ConfigureAwait(false);
            try
            {
                if (!IsAlive) throw new IOException("Codex App Server 已停止，请刷新重试。");
                var bytes = new UTF8Encoding(false).GetBytes(Json.Encode(value) + "\n");
                await process.StandardInput.BaseStream.WriteAsync(bytes, 0, bytes.Length, cancellation).ConfigureAwait(false);
                await process.StandardInput.BaseStream.FlushAsync(cancellation).ConfigureAwait(false);
            }
            finally { writer.Release(); }
        }
        private async Task ReadOutputAsync()
        {
            try
            {
                var bytes = new byte[8192];
                using (var buffer = new LineBuffer())
                {
                    int count;
                    while ((count = await process.StandardOutput.BaseStream.ReadAsync(bytes, 0, bytes.Length).ConfigureAwait(false)) > 0)
                        foreach (var line in buffer.Append(bytes, count)) HandleMessage(Json.Parse(line));
                }
            }
            catch (Exception ex)
            {
                if (ex is OutOfMemoryException) throw;
            }
            finally { Fail(); }
        }
        private async Task DrainErrorsAsync()
        {
            try
            {
                var bytes = new byte[4096];
                int count;
                while ((count = await process.StandardError.BaseStream.ReadAsync(bytes, 0, bytes.Length).ConfigureAwait(false)) > 0)
                {
                    lock (sync)
                    {
                        int retained = Math.Min(DiagnosticByteCount, diagnosticTail.Length - count);
                        if (retained > 0) Buffer.BlockCopy(diagnosticTail, DiagnosticByteCount - retained, diagnosticTail, 0, retained);
                        Buffer.BlockCopy(bytes, 0, diagnosticTail, retained, count);
                        DiagnosticByteCount = retained + count;
                    }
                }
            }
            catch (Exception ex) { if (ex is OutOfMemoryException) throw; }
        }
        private void HandleMessage(Dictionary<string, object> message)
        {
            if (message == null) return;
            var method = Json.Text(message, "method");
            if (method != null)
            {
                // Do not expose notification payloads or server-originated requests to UI/logs.
                if (Json.Get(message, "id") == null)
                {
                    var handler = Notification;
                    if (handler != null) handler(method);
                }
                return;
            }
            var id = Json.Number(message, "id");
            if (!id.HasValue || id < 1 || id > int.MaxValue || id != Math.Floor(id.Value)) return;
            TaskCompletionSource<Dictionary<string, object>> source;
            lock (sync) { if (!pending.TryGetValue((int)id.Value, out source)) return; }
            var error = Json.Object(message, "error");
            if (error != null)
            {
                double code = Json.Number(error, "code") ?? 0;
                source.TrySetException(new RpcException(code >= int.MinValue && code <= int.MaxValue ? (int)code : 0));
            }
            else
            {
                var result = Json.Object(message, "result");
                if (result != null) source.TrySetResult(result);
                else source.TrySetException(new IOException("Codex 返回了无法识别的数据，请更新 CLI 后重试。"));
            }
        }
        private void Fail()
        {
            if (Interlocked.Exchange(ref ended, 1) != 0) return;
            lock (sync)
            {
                foreach (var source in pending.Values)
                    source.TrySetException(new IOException("Codex App Server 连接已中断，请刷新重试。"));
                pending.Clear();
            }
            if (job != null) job.Dispose();
            var handler = Disconnected;
            if (handler != null) handler();
        }
        public void Dispose()
        {
            if (Interlocked.Exchange(ref disposed, 1) != 0) return;
            Fail();
            if (job != null) job.Dispose();
            process.StandardInput.Close();
            process.StandardOutput.Close();
            process.StandardError.Close();
            process.Dispose();
        }
    }
}
