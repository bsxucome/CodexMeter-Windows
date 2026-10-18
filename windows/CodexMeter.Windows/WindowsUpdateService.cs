using System;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using CodexMeter.Core;

namespace CodexMeter.Windows
{
    internal sealed class WindowsUpdateService : IDisposable
    {
        private readonly Func<Settings> settings;
        private readonly bool preview;
        private readonly Action quit;
        private readonly UpdateDownload download = new UpdateDownload();
        private CancellationTokenSource operation;
        private string manifest, stage, fingerprint;
        private DateTimeOffset nextAutomatic = DateTimeOffset.UtcNow.AddSeconds(30);
        public SignedRelease Available { get; private set; }
        public bool Busy { get; private set; }
        public bool Ready { get { return stage != null && fingerprint == Policy(settings()); } }
        public string Status { get; private set; }
        public event Action Changed;
        public WindowsUpdateService(Func<Settings> settings, bool preview, Action quit)
        { this.settings = settings; this.preview = preview; this.quit = quit; Status = preview ? "预览模式不会访问更新源或安装。" : Configured ? "尚未检查更新。" : "尚未配置 Windows 更新源。"; }
        public bool Configured { get { return !string.IsNullOrWhiteSpace(settings().UpdateFeed) && !string.IsNullOrWhiteSpace(settings().UpdatePublicKey); } }
        private static string Policy(Settings value) { return value.UpdateFeed + "\n" + value.UpdatePublicKey + "\n" + value.UpdateChannel; }
        public void Reset() { Cancel(); Available = null; manifest = null; stage = null; fingerprint = null; nextAutomatic = DateTimeOffset.UtcNow.AddSeconds(30); Status = Configured ? "更新配置已保存。" : "尚未配置 Windows 更新源。"; Raise(); }
        private void Raise() { var handler = Changed; if (handler != null) handler(); }
        public void Cancel() { if (operation != null) operation.Cancel(); }
        internal void Simulate(string state)
        {
            if (!preview) return;
            Available = state == "available" ? new SignedRelease { Version = "9.9.9", Notes = "演示更新，不会下载或安装。", Channel = "stable", Runtime = SignedRelease.CurrentRuntime } : null;
            Status = state == "available" ? "发现新版本：9.9.9 · 演示数据" : state == "error" ? "检查失败；演示错误状态。" : "当前已是此通道的最新版本。";
            Raise();
        }
        public async Task Check()
        {
            if (preview || Busy) return;
            if (!Configured) { Status = "请先填写 Windows 更新源和签名公钥。"; Raise(); return; }
            var policy = settings().Copy(); var expected = Policy(policy);
            Busy = true; Available = null; stage = null; manifest = null; Status = "正在检查 Windows 更新…"; Raise();
            using (var cancel = new CancellationTokenSource(30000))
            {
                operation = cancel;
                try
                {
                    string address = policy.UpdateFeed.Replace("{channel}", policy.UpdateChannel).Replace("{runtime}", SignedRelease.CurrentRuntime);
                    var content = await download.ManifestAsync(address, cancel.Token);
                    var release = SignedRelease.Verify(content, policy.UpdatePublicKey, policy.UpdateChannel, SignedRelease.CurrentRuntime);
                    if (expected != Policy(settings())) { Status = "更新配置已改变，请重新检查。"; return; }
                    if (ReleaseVersion.Parse(release.Version).CompareTo(ReleaseVersion.Parse(AppVersion.Value)) > 0)
                    { Available = release; manifest = content; fingerprint = expected; Status = "发现新版本：" + release.Version; }
                    else Status = "当前已是此通道的最新版本。";
                }
                catch (OperationCanceledException) { Status = "检查已取消或超时。"; }
                catch (Exception ex) { if (ex is OutOfMemoryException) throw; Status = "检查失败；请检查更新地址、公钥、通道及网络。"; }
                finally { operation = null; Busy = false; Raise(); }
            }
        }
        public async Task Download()
        {
            if (preview || Busy || Available == null || fingerprint != Policy(settings())) return;
            var release = Available; string token = Guid.NewGuid().ToString("N"); string directory = UpdateWorker.Stage(token);
            Busy = true; Status = "正在下载并校验更新…"; Raise();
            using (var cancel = new CancellationTokenSource(TimeSpan.FromMinutes(10)))
            {
                operation = cancel;
                try
                {
                    Directory.CreateDirectory(directory);
                    PortableFiles.AtomicWrite(Path.Combine(directory, "manifest.json"), Encoding.UTF8.GetBytes(manifest));
                    int lastProgress = -1;
                    var progress = new Progress<double>(value => { int percent = (int)value; if (Busy && operation == cancel && !cancel.IsCancellationRequested && percent != lastProgress) { lastProgress = percent; Status = "正在下载并校验更新… " + percent + "%"; Raise(); } });
                    await download.DownloadAsync(release, Path.Combine(directory, "package.zip"), cancel.Token, progress);
                    if (fingerprint != Policy(settings())) { Status = "更新配置已改变，请重新检查。"; return; }
                    stage = token; Status = "签名与文件校验通过，可安装并重启。";
                }
                catch (OperationCanceledException) { Status = "下载已取消或超时。"; }
                catch (Exception ex) { if (ex is OutOfMemoryException) throw; Status = "下载或校验失败；当前版本保持可用。"; }
                finally
                {
                    if (stage != token)
                    {
                        foreach (string name in new[] { "package.zip", "manifest.json" })
                            try { string file = Path.Combine(directory, name); if (File.Exists(file)) File.Delete(file); } catch (IOException) { } catch (UnauthorizedAccessException) { }
                    }
                    operation = null; Busy = false; Raise();
                }
            }
        }
        public async void Install()
        {
            if (preview || Busy || !Ready || Available == null) return;
            Busy = true; Status = "正在准备安装…"; Raise();
            using (var cancellation = new CancellationTokenSource(70000))
            {
                operation = cancellation;
                try { await UpdateWorker.Begin(stage, Available, settings().UpdatePublicKey, cancellation.Token); Busy = false; quit(); }
                catch (Exception ex) { if (ex is OutOfMemoryException) throw; Status = "无法启动更新安装；请检查同级目录的写入权限。"; }
                finally { operation = null; Busy = false; Raise(); }
            }
        }
        public async void Automatic(bool idle)
        {
            if (preview || Busy || !idle || !Configured || !settings().UpdateAutomatic || DateTimeOffset.UtcNow < nextAutomatic) return;
            nextAutomatic = DateTimeOffset.UtcNow.AddDays(1);
            await Check();
            // Installation gets a separate idle check on a subsequent tick.
            if (Available != null && settings().UpdateAutoInstall) await Download();
        }
        public void InstallIfIdle(bool idle)
        { if (!preview && idle && settings().UpdateAutomatic && settings().UpdateAutoInstall && Ready) Install(); }
        public void Dispose() { Cancel(); download.Dispose(); }
    }
}
