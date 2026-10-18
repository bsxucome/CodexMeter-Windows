using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace CodexMeter.Core
{
    public sealed class UsageState
    {
        public IList<QuotaWindow> Windows { get; set; }
        public string Plan { get; set; }
        public string Error { get; set; }
        public bool IsStale { get; set; }
        public bool IsLoading { get; set; }
        public DateTimeOffset? UpdatedAt { get; set; }
        public DateTimeOffset NextRefresh { get; set; }
        public TokenUsage Tokens { get; set; }
        public DateTimeOffset? TokensUpdatedAt { get; set; }
        public bool TokensStale { get; set; }
        public string TokenError { get; set; }
        public string HistoryError { get; set; }
        public string AccountKey { get; set; }
        public string SessionId { get; set; }
        public CreditBalance Credits { get; set; }
        public ResetCredits ResetCredits { get; set; }
        public UsageState() { Windows = new List<QuotaWindow>(); Plan = "正在检查账号"; SessionId = Guid.NewGuid().ToString("N"); }
        public UsageState Copy() { return (UsageState)MemberwiseClone(); }
    }
    public sealed class UsageService : IDisposable
    {
        private readonly object sync = new object();
        private readonly ICliLocator locator;
        private readonly IRpcFactory factory;
        private readonly CancellationTokenSource lifetime = new CancellationTokenSource();
        private readonly SemaphoreSlim refreshGate = new SemaphoreSlim(1, 1);
        private IRpcConnection connection;
        private Action<string> notificationHandler;
        private Action disconnectedHandler;
        private UsageState state = new UsageState();
        private string path = "", identity;
        private string anonymousSession = Guid.NewGuid().ToString("N");
        private DateTimeOffset nextUsageRead;
        private HistoryStore history;
        private bool historyEnabled;
        private long generation;
        private int failures, disposed;
        private bool refreshing, refreshAgain, tokenRefresh;
        public event Action Changed;
        public int RequestTimeoutMilliseconds { get; set; }
        public UsageService(ICliLocator locator, IRpcFactory factory)
        {
            this.locator = locator; this.factory = factory;
            RequestTimeoutMilliseconds = 20000;
        }
        public UsageState State { get { lock (sync) return state.Copy(); } }
        public void ConfigureHistory(HistoryStore value, int retentionDays, bool enabled)
        {
            lock (sync)
            {
                history = value;
                historyEnabled = enabled;
                state.NextRefresh = DateTimeOffset.MinValue;
                if (refreshing) refreshAgain = true;
                state.HistoryError = null;
                if (history == null) state.AccountKey = null;
                else
                {
                    try { history.SetRetention(retentionDays); }
                    catch (IOException) { state.HistoryError = "历史记录保留期限未能应用，请检查本地数据目录。"; }
                }
            }
        }
        public void Configure(string cliPath)
        {
            lock (sync)
            {
                if (path == (cliPath ?? "")) return;
                path = cliPath ?? ""; generation++;
                identity = null; state = new UsageState();
                anonymousSession = Guid.NewGuid().ToString("N"); nextUsageRead = DateTimeOffset.MinValue;
                CloseConnection(); refreshAgain = refreshing;
            }
            Raise();
        }
        private void Raise() { var handler = Changed; if (handler != null) handler(); }
        private void CloseConnection()
        {
            if (connection == null) return;
            var old = connection; connection = null;
            old.Notification -= notificationHandler; old.Disconnected -= disconnectedHandler;
            old.Dispose();
        }
        private void OnDisconnected(IRpcConnection source)
        {
            lock (sync)
            {
                if (connection != source || disposed != 0) return;
                state.IsStale = state.UpdatedAt.HasValue;
                state.Error = "Codex 连接已中断，将自动重试。";
                state.NextRefresh = DateTimeOffset.UtcNow.AddSeconds(10);
            }
            Raise();
        }
        private void OnNotification(IRpcConnection source, string method)
        {
            if (method != "account/updated" && method != "account/rateLimits/updated") return;
            lock (sync)
            {
                if (connection != source || disposed != 0) return;
                if (method == "account/updated")
                {
                    generation++; identity = null; tokenRefresh = true;
                    state = new UsageState();
                    anonymousSession = Guid.NewGuid().ToString("N"); nextUsageRead = DateTimeOffset.MinValue;
                }
                state.NextRefresh = DateTimeOffset.MinValue;
                if (refreshing) refreshAgain = true;
            }
            Raise();
            // Serialize refreshes; the active request's generation is invalid after an account boundary.
            QueueRefresh();
        }
        private async void QueueRefresh()
        {
            try { await RefreshAsync(false).ConfigureAwait(false); }
            catch (ObjectDisposedException) { }
        }
        public async Task RefreshAsync(bool force)
        {
            if (Volatile.Read(ref disposed) != 0 || !await refreshGate.WaitAsync(0).ConfigureAwait(false)) return;
            long epoch;
            lock (sync)
            {
                if (!force && DateTimeOffset.UtcNow < state.NextRefresh) { refreshGate.Release(); return; }
                refreshing = true; state.IsLoading = true; epoch = generation;
            }
            Raise();
            bool followup = false;
            using (var request = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token))
            {
                request.CancelAfter(RequestTimeoutMilliseconds);
                try
                {
                    IRpcConnection active;
                    bool initialize;
                    lock (sync)
                    {
                        if (connection != null && !connection.IsAlive) CloseConnection();
                        initialize = connection == null;
                        if (initialize)
                        {
                            connection = factory.Start(locator.Locate(path));
                            var created = connection;
                            notificationHandler = method => OnNotification(created, method);
                            disconnectedHandler = () => OnDisconnected(created);
                            connection.Notification += notificationHandler;
                            connection.Disconnected += disconnectedHandler;
                        }
                        active = connection;
                    }
                    if (initialize)
                    {
                        await active.RequestAsync("initialize", new Dictionary<string, object> {
                            { "clientInfo", new Dictionary<string, object> { { "name", "codex_meter_windows" },
                                { "title", "CodexMeter Windows" }, { "version", ProductVersion.Current } } }
                        }, request.Token).ConfigureAwait(false);
                        await active.NotifyAsync("initialized", new Dictionary<string, object>(), request.Token).ConfigureAwait(false);
                    }
                    bool refreshToken;
                    lock (sync) refreshToken = tokenRefresh;
                    var accountResult = await active.RequestAsync("account/read",
                        new Dictionary<string, object> { { "refreshToken", refreshToken } }, request.Token).ConfigureAwait(false);
                    var account = Json.Object(accountResult, "account");
                    if (account == null)
                    {
                        lock (sync)
                        {
                            if (epoch != generation) return;
                            identity = null; state.Windows = new List<QuotaWindow>();
                            state.AccountKey = null; state.Tokens = null; state.TokensUpdatedAt = null;
                            state.Credits = null; state.ResetCredits = null; state.SessionId = Guid.NewGuid().ToString("N");
                            state.TokenError = null; state.TokensStale = false;
                            anonymousSession = Guid.NewGuid().ToString("N"); nextUsageRead = DateTimeOffset.MinValue;
                            state.UpdatedAt = null; state.IsStale = false; state.Plan = "尚未登录";
                        }
                        throw new InvalidOperationException("尚未登录 Codex。请在终端运行 codex login 后点击刷新。");
                    }
                    var type = Json.Text(account, "type") ?? "unknown";
                    var nextIdentity = Identity(account);
                    lock (sync)
                    {
                        if (epoch != generation) return;
                        if (identity != null && identity != nextIdentity)
                        {
                            state.Windows = new List<QuotaWindow>(); state.UpdatedAt = null; state.IsStale = false;
                            state.Tokens = null; state.TokensUpdatedAt = null; state.TokenError = null;
                            state.TokensStale = false;
                            state.Credits = null; state.ResetCredits = null; state.SessionId = Guid.NewGuid().ToString("N");
                            anonymousSession = Guid.NewGuid().ToString("N"); nextUsageRead = DateTimeOffset.MinValue;
                        }
                        identity = nextIdentity; tokenRefresh = false;
                        state.AccountKey = history == null ? null : history.AccountKey(type, Json.Text(account, "email"), anonymousSession);
                        state.Plan = type == "chatgpt" ? "ChatGPT · " + (Json.Text(account, "planType") ?? "未知套餐") :
                            type == "apiKey" ? "API Key" : type == "amazonBedrock" ? "Bedrock" : "Codex 账号";
                    }
                    Raise();
                    var limits = await active.RequestAsync("account/rateLimits/read",
                        new Dictionary<string, object>(), request.Token).ConfigureAwait(false);
                    var windows = QuotaParser.Parse(limits);
                    var credits = CreditBalance.Parse(limits); var resetCredits = ResetCredits.Parse(limits);
                    if (windows.Count == 0 && credits == null && resetCredits == null) throw new InvalidOperationException("此账号未返回可用额度。请确认 CLI 登录方式与账号支持额度查询。");
                    lock (sync)
                    {
                        if (epoch != generation) return;
                        AnchorCycles(windows, DateTimeOffset.UtcNow);
                        state.Windows = windows; state.UpdatedAt = DateTimeOffset.UtcNow;
                        state.Credits = credits; state.ResetCredits = resetCredits;
                        state.Error = null; state.IsStale = false; failures = 0;
                        state.NextRefresh = DateTimeOffset.UtcNow.AddSeconds(60);
                        RecordHistory(state.UpdatedAt.Value, windows, null);
                    }
                    Raise();
                    bool readUsage;
                    lock (sync) readUsage = force || DateTimeOffset.UtcNow >= nextUsageRead;
                    if (readUsage)
                    {
                        TokenUsage usage = null; string usageError = null;
                        using (var optional = CancellationTokenSource.CreateLinkedTokenSource(request.Token))
                        {
                            optional.CancelAfter(5000);
                            try
                            {
                                usage = TokenUsage.Parse(await active.RequestAsync("account/usage/read",
                                    new Dictionary<string, object>(), optional.Token).ConfigureAwait(false));
                                if (!usage.Available) usageError = "服务未返回 Token 活动。";
                            }
                            catch (RpcException) { usageError = "当前 CLI 或账号不支持 Token 活动。"; }
                            catch (OperationCanceledException) { usageError = "Token 活动查询超时，将稍后重试。"; }
                            catch (IOException) { usageError = "Token 活动暂时无法读取。"; }
                        }
                        lock (sync)
                        {
                            if (epoch != generation) return;
                            nextUsageRead = DateTimeOffset.UtcNow.AddSeconds(60);
                            state.TokenError = usageError;
                            state.TokensStale = usageError != null && state.Tokens != null;
                            if (usage != null && usage.Available)
                            {
                                state.Tokens = usage; state.TokensUpdatedAt = DateTimeOffset.UtcNow;
                                RecordHistory(state.UpdatedAt.Value, windows, usage);
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    if (ex is OutOfMemoryException) throw;
                    lock (sync)
                    {
                        if (epoch == generation && !lifetime.IsCancellationRequested)
                        {
                            failures++;
                            var diagnostics = connection as RpcConnection;
                            bool homeUnavailable = diagnostics != null && diagnostics.DiagnosticCategory == "home-unavailable";
                            state.IsStale = state.UpdatedAt.HasValue;
                            state.Error = ex is OperationCanceledException ? "请求超时，将自动重试；也可以手动刷新。" :
                                ex is RpcException ? "Codex 无法读取额度，请确认登录状态并更新 CLI 后重试。" :
                                ex is InvalidOperationException ? ex.Message :
                                homeUnavailable ? "Codex 无法访问用户主目录，请在正常 Windows 登录会话中运行应用。" :
                                "无法连接 Codex App Server，请检查 CLI 路径后重试。";
                            state.NextRefresh = DateTimeOffset.UtcNow.AddSeconds(Math.Min(300, 10 * Math.Pow(2, Math.Min(failures - 1, 5))));
                            CloseConnection();
                        }
                    }
                }
                finally
                {
                    lock (sync)
                    {
                        refreshing = false; state.IsLoading = false;
                        followup = refreshAgain; refreshAgain = false;
                        if (followup) state.NextRefresh = DateTimeOffset.MinValue;
                    }
                    refreshGate.Release();
                    Raise();
                    if (followup && !lifetime.IsCancellationRequested) QueueRefresh();
                }
            }
        }
        public void SetVisible(bool visible)
        {
            if (!visible) return;
            lock (sync)
            {
                if (failures == 0 && state.UpdatedAt.HasValue)
                {
                    var due = state.UpdatedAt.Value.AddSeconds(10);
                    if (due < state.NextRefresh) state.NextRefresh = due;
                }
            }
        }
        public async Task WithHistoryPausedAsync(Action maintenance)
        {
            await refreshGate.WaitAsync(lifetime.Token).ConfigureAwait(false);
            try
            {
                lock (sync) { history = null; historyEnabled = false; state.AccountKey = null; state.SessionId = Guid.NewGuid().ToString("N"); state.IsLoading = true; }
                Raise(); await Task.Run(maintenance).ConfigureAwait(false);
            }
            finally
            {
                lock (sync) { state.IsLoading = false; state.NextRefresh = DateTimeOffset.MinValue; }
                refreshGate.Release(); Raise();
            }
        }
        // Called only while holding sync and after the account generation check.
        private void AnchorCycles(IList<QuotaWindow> windows, DateTimeOffset now)
        {
            foreach (var window in windows)
            {
                var previous = state.Windows.FirstOrDefault(w => w.Id == window.Id);
                QuotaSample anchor = previous != null && state.UpdatedAt.HasValue ? new QuotaSample { At = state.UpdatedAt.Value,
                    Remaining = previous.RemainingPercent, ResetsAt = previous.CycleResetsAt ?? previous.ResetsAt, DurationMinutes = previous.DurationMinutes } : null;
                if (anchor == null && history != null && state.AccountKey != null)
                {
                    try { anchor = history.LatestQuota(state.AccountKey, window.Id); }
                    catch (IOException) { }
                    catch (UnauthorizedAccessException) { }
                }
                window.CycleResetsAt = anchor == null ? window.ResetsAt : QuotaCycle.NormalizeReset(anchor.At, anchor.Remaining, anchor.ResetsAt,
                    anchor.DurationMinutes, now, window.RemainingPercent, window.ResetsAt, window.DurationMinutes);
            }
        }
        private void RecordHistory(DateTimeOffset at, IList<QuotaWindow> windows, TokenUsage usage)
        {
            if (history == null || !historyEnabled || state.AccountKey == null) return;
            try { history.Record(state.AccountKey, at, windows, usage); state.HistoryError = null; }
            catch (IOException) { state.HistoryError = "本地历史记录未能保存，实时额度仍可使用。"; }
            catch (UnauthorizedAccessException) { state.HistoryError = "没有保存本地历史记录的权限，实时额度仍可使用。"; }
        }
        private static string Identity(Dictionary<string, object> account)
        {
            var value = (Json.Text(account, "type") ?? "") + "|" + (Json.Text(account, "email") ?? "").Trim().ToLowerInvariant();
            using (var hash = SHA256.Create()) return Convert.ToBase64String(hash.ComputeHash(Encoding.UTF8.GetBytes(value)));
        }
        public void Dispose()
        {
            if (Interlocked.Exchange(ref disposed, 1) != 0) return;
            lifetime.Cancel();
            lock (sync) { generation++; CloseConnection(); }
        }
    }
}
