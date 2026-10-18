using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using CodexMeter.Core;

namespace CodexMeter.Windows
{
    internal sealed partial class TrayApplication
    {
        private RemoteProfile remoteProfile = new RemoteProfile();
        private ISecretVault vault;
        private readonly RemoteDelivery remoteDelivery = new RemoteDelivery();
        private readonly ReplenishmentRules replenishments = new ReplenishmentRules();
        private CancellationTokenSource remoteEpoch = new CancellationTokenSource();
        private readonly SemaphoreSlim remoteGate = new SemaphoreSlim(1, 1);
        private string remoteSession;
        private int remotePending;
        private readonly Dictionary<string, string> remoteStatus = new Dictionary<string, string>();
        private RemoteWindow remoteWindow;
        private DataWindow dataWindow;
        private AboutWindow aboutWindow;
        private WindowsUpdateService updater;
        private bool dataBusy;
        private void InitializeFeatures()
        {
            updater = new WindowsUpdateService(() => configuration, preview, Quit);
            if (preview) return;
            vault = new WindowsSecretVault(Path.Combine(Path.GetDirectoryName(store.FilePath), "remote-secrets.bin"));
            try { remoteProfile = RemoteProfile.Load(vault); }
            catch (Exception ex) { if (ex is OutOfMemoryException) throw; settingsWarning = "远程提醒配置无法解密或校验，渠道已暂时停用。"; }
        }
        private void ResetRemote()
        {
            remoteEpoch.Cancel(); remoteEpoch.Dispose(); remoteEpoch = new CancellationTokenSource(); replenishments.Reset();
        }
        private void EvaluateRemote(UsageState state)
        {
            if (dataBusy) return;
            if (remoteSession != state.SessionId) { ResetRemote(); remoteSession = state.SessionId; }
            foreach (var change in replenishments.Evaluate(state, DateTimeOffset.UtcNow))
                foreach (var channel in remoteProfile.Channels)
                    if (channel.Enabled && channel.Includes(change.WindowId) && remotePending < 32) DeliverRemote(channel.Copy(), change, remoteEpoch.Token);
        }
        private async void DeliverRemote(RemoteChannel channel, Replenishment change, CancellationToken token)
        {
            remotePending++; bool entered = false;
            try
            {
                await remoteGate.WaitAsync(token); entered = true;
                Func<bool> valid = () => { var state = service.State; return !quitting && !dataBusy && !state.IsStale && state.SessionId == change.SessionId && DateTimeOffset.UtcNow - change.At < TimeSpan.FromMinutes(2); };
                var result = await remoteDelivery.SendAsync(channel, L.T("额度已补充"),
                    L.T(change.Title) + " · " + change.Before.ToString("0.#") + "% → " + change.After.ToString("0.#") + "%", token, valid);
                if (!token.IsCancellationRequested && valid())
                { remoteStatus[channel.Kind] = result.Status; if (remoteWindow != null) remoteWindow.SetStatus(channel.Kind, result.Status); }
            }
            catch (OperationCanceledException) { }
            catch (ObjectDisposedException) { }
            finally { remotePending--; if (entered) remoteGate.Release(); }
        }
        private void ShowRemote()
        {
            if (quitting || dataBusy) return;
            if (remoteWindow != null) { remoteWindow.Activate(); return; }
            remoteWindow = new RemoteWindow(configuration.Theme, preview, remoteProfile.Copy(), next => {
                if (!preview) next.Save(vault);
                remoteProfile = next.Copy(); ResetRemote(); remoteStatus.Clear();
            }, async (channel, token) => {
                if (preview) return new DeliveryResult { Status = "预览模式不会发送远程消息。" };
                return await remoteDelivery.SendAsync(channel, "CodexMeter", L.T("这是一条远程提醒测试消息。"), token);
            });
            foreach (var pair in remoteStatus) remoteWindow.SetStatus(pair.Key, pair.Value);
            remoteWindow.Closed += (s, e) => remoteWindow = null; remoteWindow.Show(); remoteWindow.Activate();
        }
        private void ShowData()
        {
            if (quitting || dataBusy) return;
            if (dataWindow != null) { dataWindow.Activate(); return; }
            dataWindow = new DataWindow(configuration.Theme, preview, () => dataBusy ? null : history,
                () => preview ? null : service.State.AccountKey, RestoreHistory, () => { if (historyWindow != null) historyWindow.Close(); Render(); });
            dataWindow.Closed += (s, e) => dataWindow = null; dataWindow.Show(); dataWindow.Activate();
        }
        private async Task RestoreHistory(string archive)
        {
            if (preview || dataBusy) return;
            if (updater.Busy) throw new IOException("请等待更新操作完成。");
            dataBusy = true; ResetRemote();
            if (historyWindow != null) historyWindow.Close(); if (settings != null) settings.IsEnabled = false;
            try
            {
                await service.WithHistoryPausedAsync(() => {
                    string directory = Path.GetDirectoryName(store.FilePath);
                    using (var prepared = HistoryBackup.Prepare(archive, Path.Combine(directory, "restore")))
                    {
                        if (history == null) history = new HistoryStore(Path.Combine(directory, "history.sqlite3"));
                        history.RestoreSnapshot(prepared.DatabasePath, Path.Combine(directory, "backups", "before-restore-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + "-" + Guid.NewGuid().ToString("N").Substring(0, 8) + ".codexbackup"));
                    }
                });
            }
            finally
            {
                try { if (history != null) { history.Dispose(); history = null; } ConfigureHistory(); }
                finally { dataBusy = false; if (settings != null) settings.IsEnabled = true; Render(); Refresh(); }
            }
        }
        private static void CopyUpdateSettings(Settings source, Settings destination)
        {
            destination.UpdateFeed = source.UpdateFeed; destination.UpdatePublicKey = source.UpdatePublicKey;
            destination.UpdateChannel = source.UpdateChannel; destination.UpdateAutomatic = source.UpdateAutomatic; destination.UpdateAutoInstall = source.UpdateAutoInstall;
        }
        private void ShowAbout()
        {
            if (quitting || dataBusy) return;
            if (aboutWindow != null) { aboutWindow.Activate(); return; }
            aboutWindow = new AboutWindow(configuration.Copy(), preview, updater, next => {
                var merged = configuration.Copy(); CopyUpdateSettings(next, merged); if (!preview) store.Save(merged); configuration = merged;
            });
            aboutWindow.Closed += (s, e) => aboutWindow = null; aboutWindow.Show(); aboutWindow.Activate();
        }
        private void CloseFeatures()
        {
            remoteEpoch.Cancel(); updater.Cancel();
            if (remoteWindow != null) remoteWindow.Close(); if (dataWindow != null) dataWindow.Close(); if (aboutWindow != null) aboutWindow.Close();
        }
    }
}
