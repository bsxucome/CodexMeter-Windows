using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace CodexMeter.Core
{
    public sealed class RemoteChannel
    {
        public string Kind { get; set; }
        public bool Enabled { get; set; }
        public string Endpoint { get; set; }
        public string Secret { get; set; }
        public string Recipient { get; set; }
        public string IconUrl { get; set; }
        public int Priority { get; set; }
        public bool FiveHour { get; set; }
        public bool Weekly { get; set; }
        public RemoteChannel Copy() { return (RemoteChannel)MemberwiseClone(); }
        public RemoteChannel() { Endpoint = ""; Secret = ""; Recipient = ""; IconUrl = ""; Priority = 5; FiveHour = true; Weekly = true; }
        public bool Includes(string windowId) { return (FiveHour && windowId == "codex:300") || (Weekly && windowId == "codex:10080"); }
    }
    public sealed class RemoteProfile
    {
        public IList<RemoteChannel> Channels { get; private set; }
        public RemoteProfile() { Channels = new[] { "Bark", "Gotify", "Telegram", "Discord" }.Select(k => new RemoteChannel { Kind = k, Endpoint = k == "Bark" ? "https://api.day.app" : "" }).ToList(); }
        public RemoteProfile Copy() { var copy = new RemoteProfile(); copy.Channels = Channels.Select(c => c.Copy()).ToList(); return copy; }
        public static RemoteProfile Load(ISecretVault vault)
        {
            var profile = new RemoteProfile(); var bytes = vault.Read(); if (bytes == null) return profile;
            try
            {
                var map = Json.Parse(Encoding.UTF8.GetString(bytes));
                foreach (var channel in profile.Channels)
                {
                    var data = Json.Object(map, channel.Kind); if (data == null) continue;
                    channel.Enabled = object.Equals(Json.Get(data, "enabled"), true);
                    channel.Endpoint = Json.Text(data, "endpoint") ?? channel.Endpoint; channel.Secret = Json.Text(data, "secret") ?? ""; channel.Recipient = Json.Text(data, "recipient") ?? "";
                    channel.IconUrl = Json.Text(data, "iconUrl") ?? ""; channel.Priority = (int)Math.Max(0, Math.Min(10, Json.Number(data, "priority") ?? 5));
                    channel.FiveHour = !object.Equals(Json.Get(data, "fiveHour"), false); channel.Weekly = !object.Equals(Json.Get(data, "weekly"), false);
                    if (channel.Enabled) RemoteDelivery.Validate(channel);
                }
                return profile;
            }
            finally { Array.Clear(bytes, 0, bytes.Length); }
        }
        public void Save(ISecretVault vault)
        {
            var map = new Dictionary<string, object>();
            foreach (var channel in Channels)
            {
                if (channel.Enabled) RemoteDelivery.Validate(channel);
                map[channel.Kind] = new Dictionary<string, object> { { "enabled", channel.Enabled }, { "endpoint", channel.Endpoint }, { "secret", channel.Secret }, { "recipient", channel.Recipient },
                    { "iconUrl", channel.IconUrl }, { "priority", channel.Priority }, { "fiveHour", channel.FiveHour }, { "weekly", channel.Weekly } };
            }
            var bytes = Encoding.UTF8.GetBytes(Json.Encode(map)); try { vault.Write(bytes); } finally { Array.Clear(bytes, 0, bytes.Length); }
        }
    }
    public sealed class Replenishment
    {
        public string SessionId, WindowId, Title;
        public double Before, After;
        public DateTimeOffset At;
    }
    public sealed class ReplenishmentRules
    {
        private sealed class Sample { public double Remaining; public DateTimeOffset At; }
        private readonly Dictionary<string, Sample> previous = new Dictionary<string, Sample>();
        private readonly Dictionary<string, DateTimeOffset> sent = new Dictionary<string, DateTimeOffset>();
        private string session;
        public void Reset() { previous.Clear(); sent.Clear(); session = null; }
        public IList<Replenishment> Evaluate(UsageState state, DateTimeOffset now)
        {
            var output = new List<Replenishment>();
            if (session != state.SessionId) { Reset(); session = state.SessionId; }
            if (state.IsLoading) return output;
            if (state.IsStale || !state.UpdatedAt.HasValue || state.UpdatedAt > now || now - state.UpdatedAt.Value > TimeSpan.FromMinutes(2))
            { previous.Clear(); return output; }
            foreach (var missing in previous.Keys.Except(state.Windows.Select(w => w.Id)).ToArray()) previous.Remove(missing);
            foreach (var window in state.Windows)
            {
                Sample old; DateTimeOffset last;
                if (previous.TryGetValue(window.Id, out old))
                {
                    if (state.UpdatedAt <= old.At) continue;
                    if (state.UpdatedAt.Value - old.At <= TimeSpan.FromMinutes(2) && window.RemainingPercent > old.Remaining + .01 &&
                        (!sent.TryGetValue(window.Id, out last) || now - last >= TimeSpan.FromMinutes(5)))
                    {
                        output.Add(new Replenishment { SessionId = session, WindowId = window.Id, Title = window.Title, Before = old.Remaining, After = window.RemainingPercent, At = state.UpdatedAt.Value });
                        sent[window.Id] = now;
                    }
                }
                previous[window.Id] = new Sample { Remaining = window.RemainingPercent, At = state.UpdatedAt.Value };
            }
            return output;
        }
    }
    public sealed class DeliveryResult
    {
        public bool Success;
        public int Attempts;
        public string Status;
    }
    public sealed class RemoteDelivery : IDisposable
    {
        private readonly HttpClient client;
        public Func<TimeSpan, CancellationToken, Task> Delay { get; set; }
        public RemoteDelivery(HttpMessageHandler handler = null)
        {
            client = new HttpClient(handler ?? new HttpClientHandler { AllowAutoRedirect = false }) { Timeout = Timeout.InfiniteTimeSpan };
            Delay = (time, token) => Task.Delay(time, token);
        }
        public static Uri Server(string value)
        {
            Uri uri;
            if (value == null || value.Length > 2048 || !Uri.TryCreate(value, UriKind.Absolute, out uri) || !string.IsNullOrEmpty(uri.UserInfo) ||
                !string.IsNullOrEmpty(uri.Query) || !string.IsNullOrEmpty(uri.Fragment) || (uri.Scheme != "https" && !(uri.Scheme == "http" && uri.IsLoopback)))
                throw new ArgumentException("请输入 HTTPS 服务地址；本机服务可使用 HTTP。");
            return uri;
        }
        public static void Validate(RemoteChannel channel)
        {
            if (channel.Priority < 0 || channel.Priority > 10) throw new ArgumentException("渠道配置无效");
            if (!string.IsNullOrWhiteSpace(channel.IconUrl))
            { try { SignedRelease.Https(channel.IconUrl); } catch (System.IO.InvalidDataException) { throw new ArgumentException("渠道配置无效"); } }
            if (string.IsNullOrWhiteSpace(channel.Secret) || channel.Secret.Length > 2048 || channel.Secret.Any(char.IsControl)) throw new ArgumentException("请填写有效的渠道凭据。");
            if (channel.Kind == "Bark" || channel.Kind == "Gotify") Server(channel.Endpoint);
            else if (channel.Kind == "Telegram")
            {
                if (!Regex.IsMatch(channel.Secret, @"^\d+:[A-Za-z0-9_-]+$") || string.IsNullOrWhiteSpace(channel.Recipient) || channel.Recipient.Length > 128 || channel.Recipient.Any(char.IsControl))
                    throw new ArgumentException("请填写有效的 Bot Token 和 Chat ID。");
            }
            else if (channel.Kind == "Discord")
            {
                var uri = Server(channel.Secret);
                if (uri.Scheme != "https" || (uri.Host != "discord.com" && uri.Host != "canary.discord.com" && uri.Host != "ptb.discord.com") ||
                    !Regex.IsMatch(uri.AbsolutePath, @"^/api(?:/v\d+)?/webhooks/\d+/[A-Za-z0-9_-]+$")) throw new ArgumentException("请填写 Discord Webhook 地址。");
            }
            else throw new ArgumentException("不支持此远程渠道。");
        }
        public static HttpRequestMessage Request(RemoteChannel channel, string title, string text)
        {
            Validate(channel); var body = new Dictionary<string, object>(); string address;
            title = Clean(title, 100); text = Clean(text, 1500);
            if (channel.Kind == "Bark") { address = channel.Endpoint.TrimEnd('/') + "/push"; body["device_key"] = channel.Secret; body["title"] = title; body["body"] = text; body["group"] = "CodexMeter"; if (!string.IsNullOrWhiteSpace(channel.IconUrl)) body["icon"] = channel.IconUrl; }
            else if (channel.Kind == "Gotify") { address = channel.Endpoint.TrimEnd('/') + "/message"; body["title"] = title; body["message"] = text; body["priority"] = channel.Priority; }
            else if (channel.Kind == "Telegram") { address = "https://api.telegram.org/bot" + channel.Secret + "/sendMessage"; body["chat_id"] = channel.Recipient; body["text"] = title + "\n" + text; body["link_preview_options"] = new Dictionary<string, object> { { "is_disabled", true } }; }
            else { address = channel.Secret + "?wait=true"; body["content"] = title + "\n" + text; body["allowed_mentions"] = new Dictionary<string, object> { { "parse", new string[0] } }; }
            var request = new HttpRequestMessage(HttpMethod.Post, address) { Content = new StringContent(Json.Encode(body), Encoding.UTF8, "application/json") };
            if (channel.Kind == "Gotify") request.Headers.Add("X-Gotify-Key", channel.Secret);
            return request;
        }
        private static string Clean(string value, int maximum)
        { value = new string((value ?? "").Where(c => !char.IsControl(c)).ToArray()); return value.Substring(0, Math.Min(value.Length, maximum)); }
        public async Task<DeliveryResult> SendAsync(RemoteChannel channel, string title, string text, CancellationToken token, Func<bool> stillValid = null)
        {
            var result = new DeliveryResult { Status = "发送失败" };
            for (int attempt = 1; attempt <= 3; attempt++)
            {
                if (token.IsCancellationRequested || (stillValid != null && !stillValid())) { result.Status = "已取消"; return result; }
                result.Attempts = attempt;
                using (var timeout = CancellationTokenSource.CreateLinkedTokenSource(token))
                {
                    timeout.CancelAfter(8000);
                    try
                    {
                        using (var request = Request(channel, title, text))
                        using (var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeout.Token).ConfigureAwait(false))
                        using (timeout.Token.Register(() => response.Dispose()))
                        {
                            int code = (int)response.StatusCode;
                            if (code == 429 || code >= 500)
                            {
                                result.Status = code == 429 ? "服务限流" : "服务暂时不可用";
                                if (attempt == 3) return result;
                                double seconds = response.Headers.RetryAfter != null && response.Headers.RetryAfter.Delta.HasValue ? response.Headers.RetryAfter.Delta.Value.TotalSeconds : attempt * 2;
                                await Delay(TimeSpan.FromSeconds(Math.Max(1, Math.Min(30, seconds))), token).ConfigureAwait(false); continue;
                            }
                            if (!response.IsSuccessStatusCode) { result.Status = code == 401 || code == 403 ? "凭据被拒绝" : code >= 300 && code < 400 ? "服务重定向被拒绝" : "请求被拒绝"; return result; }
                            using (var input = await response.Content.ReadAsStreamAsync().ConfigureAwait(false))
                            {
                                byte[] bytes = await ReadResponse(input, timeout.Token).ConfigureAwait(false);
                                var data = Json.Parse(Encoding.UTF8.GetString(bytes));
                                result.Success = channel.Kind == "Bark" ? Json.Number(data, "code") == 200 : channel.Kind == "Telegram" ? object.Equals(Json.Get(data, "ok"), true) :
                                    channel.Kind == "Gotify" ? Json.Number(data, "id") > 0 : !string.IsNullOrEmpty(Json.Text(data, "id"));
                            }
                            result.Status = result.Success ? "发送成功" : "服务未确认接收"; return result;
                        }
                    }
                    catch (OperationCanceledException) { result.Status = token.IsCancellationRequested ? "已取消" : "发送结果未知（超时）"; return result; }
                    catch (HttpRequestException) { result.Status = "发送结果未知（网络中断）"; return result; }
                    catch (System.IO.IOException) { result.Status = "服务响应无效"; return result; }
                    catch (ArgumentException) { result.Status = "渠道配置无效"; return result; }
                    catch (InvalidOperationException) { result.Status = "服务响应无效"; return result; }
                    catch (FormatException) { result.Status = "服务响应无效"; return result; }
                    catch (Exception ex) { if (ex.GetType().Name != "JsonException" && !(ex is System.Security.SecurityException)) throw; result.Status = "服务响应无效"; return result; }
                }
            }
            return result;
        }
        private static async Task<byte[]> ReadResponse(System.IO.Stream stream, CancellationToken token)
        {
            using (var memory = new System.IO.MemoryStream())
            {
                var buffer = new byte[4096]; int count;
                while ((count = await stream.ReadAsync(buffer, 0, buffer.Length, token).ConfigureAwait(false)) > 0)
                { if (memory.Length + count > 65536) throw new System.IO.InvalidDataException(); memory.Write(buffer, 0, count); }
                return memory.ToArray();
            }
        }
        public void Dispose() { client.Dispose(); }
    }
}
