using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using CodexMeter.Core;

namespace CodexMeter.Windows
{
    internal sealed class DataWindow : CompactWindow
    {
        private bool busy;
        public DataWindow(string theme, bool preview, Func<HistoryStore> store, Func<string> currentAccount,
            Func<string, Task> restore, Action changed) : base("历史与备份", theme, preview)
        {
            Body.Children.Add(Theme.Label("历史备份包含全部账号的额度、Token 记录和本地身份盐，不包含登录凭据、远程渠道凭据或偏好设置。", 11, Theme.Muted));
            var summary = Theme.Label("", 12, Theme.Text); summary.Margin = new Thickness(0, 12, 0, 0); Body.Children.Add(summary);
            Section("历史备份");
            var status = Theme.Label(preview ? "预览模式不会访问正式历史。" : "", 11, Theme.Muted); Actions.Children.Add(status);
            var export = ActionButton("导出历史备份…", async (s, e) => {
                var picker = new Microsoft.Win32.SaveFileDialog { Filter = "CodexMeter 历史备份|*.codexbackup", DefaultExt = ".codexbackup", FileName = "CodexMeter-history-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") };
                if (picker.ShowDialog(this) != true) return;
                await Work(async () => { await Task.Run(() => HistoryBackup.Create(store(), picker.FileName)); status.Text = L.T("历史备份已导出。"); });
            });
            export.IsEnabled = !preview && store() != null; Body.Children.Add(export);
            var restoreButton = ActionButton("恢复历史备份…", async (s, e) => {
                var picker = new Microsoft.Win32.OpenFileDialog { Filter = "CodexMeter 历史备份|*.codexbackup", CheckFileExists = true };
                if (picker.ShowDialog(this) != true) return;
                await Work(async () => {
                    using (var prepared = await Task.Run(() => HistoryBackup.Prepare(picker.FileName, System.IO.Path.Combine(System.IO.Path.GetTempPath(), "CodexMeter-restore-validation"))))
                    {
                        string counts = L.T("本地账号：") + prepared.Info.Accounts + " · " + prepared.Info.QuotaRows + " / " + prepared.Info.TokenRows;
                        if (L.Confirm(this, counts + "\n" + L.T("恢复会替换全部本地账号历史，并沿用当前保留期限。开始前会自动备份现有记录；是否继续？"), "恢复历史备份") != MessageBoxResult.Yes) { status.Text = L.T("已取消"); return; }
                    }
                    await restore(picker.FileName); status.Text = L.T("历史已恢复，恢复前备份已保留。"); RefreshSummary();
                });
            });
            restoreButton.IsEnabled = !preview; Body.Children.Add(restoreButton);
            Section("本地账号管理");
            var accounts = Theme.Combo(); accounts.Margin = new Thickness(0, 6, 0, 10); Body.Children.Add(accounts);
            var clear = ActionButton("清除所选账号历史…", async (s, e) => {
                if (accounts.SelectedIndex < 0) return;
                string key = (string)((ComboBoxItem)accounts.SelectedItem).Tag;
                if (L.Confirm(this, "仅清除所选账号的本地历史，是否继续？", "清除历史") != MessageBoxResult.Yes) return;
                await Work(async () => { await Task.Run(() => store().Clear(key)); changed(); RefreshSummary(); status.Text = L.T("所选账号历史已清除。"); });
            });
            clear.IsEnabled = !preview && store() != null; Body.Children.Add(clear);
            var clearAll = ActionButton("清除全部账号历史…", async (s, e) => {
                if (L.Confirm(this, "将清除全部账号的额度和 Token 历史；建议先导出备份。是否继续？", "清除全部历史") != MessageBoxResult.Yes) return;
                await Work(async () => { await Task.Run(() => store().ClearAll()); changed(); RefreshSummary(); status.Text = L.T("全部历史已清除。"); });
            });
            clearAll.IsEnabled = !preview && store() != null; Body.Children.Add(clearAll);
            Actions.Children.Add(ActionButton("完成", (s, e) => Close()));
            Closing += (s, e) => { if (busy) e.Cancel = true; };
            refreshSummary = () => {
                if (preview || store() == null) { summary.Text = L.T(preview ? "预览模式 · 演示数据" : "尚无历史数据库。"); return; }
                var rows = store().Accounts(); summary.Text = L.T("本地账号：") + rows.Count + " · " + (store().SizeBytes / 1024.0).ToString("N1") + " KB";
                accounts.Items.Clear(); int index = 0;
                foreach (var row in rows)
                    accounts.Items.Add(new ComboBoxItem { Content = (row.Key == currentAccount() ? L.T("当前账号") : L.T("本地账号 ") + (++index)) + " · " + row.QuotaRows + " / " + row.TokenRows, Tag = row.Key });
                if (accounts.Items.Count > 0) accounts.SelectedIndex = 0;
            };
            work = async action => {
                if (busy) return; busy = true; Body.IsEnabled = false; status.Text = L.T("正在处理…");
                try { await action(); }
                catch (Exception ex) { if (ex is OutOfMemoryException) throw; status.Text = L.T("历史操作失败；现有记录与恢复前备份会保留，请检查文件和目录权限。"); }
                finally { busy = false; Body.IsEnabled = true; }
            };
            RefreshSummary(); FinishLayout();
        }
        private Action refreshSummary;
        private Func<Func<Task>, Task> work;
        private void RefreshSummary() { refreshSummary(); }
        private Task Work(Func<Task> action) { return work(action); }
    }
}
