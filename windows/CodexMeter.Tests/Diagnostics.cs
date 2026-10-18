using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using CodexMeter.Core;

namespace CodexMeter.Tests
{
    internal static class Diagnostics
    {
        public static async Task TransportProbe(string path)
        {
            using (var connection = new RpcConnection(new CliLocator().Locate(path)))
            using (var timeout = new CancellationTokenSource(20000))
            {
                try
                {
                    await connection.RequestAsync("initialize", new Dictionary<string, object> {
                        { "clientInfo", new Dictionary<string, object> { { "name", "codex_meter_windows" }, { "version", "0.1.0" } } }
                    }, timeout.Token);
                    Console.WriteLine("initialize=ok");
                    await connection.NotifyAsync("initialized", new Dictionary<string, object>(), timeout.Token);
                    var result = await connection.RequestAsync("account/read", new Dictionary<string, object> { { "refreshToken", false } }, timeout.Token);
                    Console.WriteLine("accountPresent=" + (Json.Object(result, "account") != null));
                    var limits = await connection.RequestAsync("account/rateLimits/read", new Dictionary<string, object>(), timeout.Token);
                    Console.WriteLine("quotaWindows=" + QuotaParser.Parse(limits).Count);
                }
                catch (Exception ex)
                {
                    Console.WriteLine("transportFailure=" + ex.GetType().Name);
                    var rpc = ex as RpcException;
                    if (rpc != null) Console.WriteLine("rpcCode=" + rpc.Code);
                    connection.Completion.Wait(500);
                    Console.WriteLine("alive=" + connection.IsAlive + ", stderrBytes=" + connection.DiagnosticByteCount +
                        ", diagnostic=" + connection.DiagnosticCategory);
                    throw new Exception("Real transport was not verified.");
                }
            }
        }
    }
}
