using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading;
using CodexMeter.Core;

namespace CodexMeter.Tests
{
    internal static class MockServer
    {
        public const string Limits = "{\"rateLimitsByLimitId\":{\"codex\":{\"primary\":{\"usedPercent\":25,\"windowDurationMins\":300},\"secondary\":{\"usedPercent\":35,\"windowDurationMins\":10080}},\"other\":{\"primary\":{\"usedPercent\":90,\"windowDurationMins\":10080}}}}";
        public static int Run(string[] args)
        {
            string mode = args.Length > 1 ? args[1] : "normal";
            string first = null;
            Console.InputEncoding = new UTF8Encoding(false); Console.OutputEncoding = new UTF8Encoding(false);
            string line;
            while ((line = Console.ReadLine()) != null)
            {
                if (mode == "exit") return 0;
                if (mode == "hang") { Thread.Sleep(10000); continue; }
                if (mode == "oversize") { Console.Write(new string('x', LineBuffer.MaximumLineBytes + 1)); Console.Out.Flush(); continue; }
                if (mode == "stderr") { Console.Error.Write(new string('e', 100000)); Console.Error.Flush(); }
                var request = Json.Parse(line);
                var id = Json.Get(request, "id");
                if (id == null) continue;
                string method = Json.Text(request, "method");
                object result = method == "account/read" ? Json.Parse("{\"account\":{\"type\":\"chatgpt\",\"planType\":\"plus\"}}") :
                    method == "account/rateLimits/read" ? Json.Parse(Limits) : new Dictionary<string, object> { { "method", method } };
                if (mode == "tree")
                {
                    var command = Program.Mock("hang");
                    var child = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo {
                        FileName = command.Executable, Arguments = command.Arguments(), UseShellExecute = false,
                        CreateNoWindow = true, RedirectStandardInput = true
                    });
                    result = new Dictionary<string, object> { { "pid", child.Id } };
                }
                string response = mode == "rpc-error" ?
                    Json.Encode(new Dictionary<string, object> { { "id", id }, { "error", new Dictionary<string, object> {
                        { "code", -32603 }, { "message", "PRIVATE_SENTINEL" } } } }) :
                    Json.Encode(new Dictionary<string, object> { { "id", id }, { "result", result } });
                if (mode == "reorder" && first == null) { first = response; continue; }
                if (mode == "fragment")
                {
                    foreach (char character in response) { Console.Write(character); Console.Out.Flush(); }
                    Console.WriteLine();
                }
                else Console.WriteLine(response);
                if (first != null) { Console.WriteLine(first); first = null; }
                Console.Out.Flush();
            }
            return 0;
        }
    }
}
