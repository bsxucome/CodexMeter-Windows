using System;
using System.Windows;
using System.Windows.Controls;
using CodexMeter.Core;

namespace CodexMeter.Windows
{
    internal sealed class AboutWindow : CompactWindow
    {
        public AboutWindow(Settings current, bool preview, WindowsUpdateService updater, Action<Settings> save) : base("关于与更新", current.Theme, preview)
        {
            var version = Theme.Label("CodexMeter for Windows  " + AppVersion.Value, 13, Theme.Text); version.FontWeight = FontWeights.SemiBold; Body.Children.Add(version);
            Body.Children.Add(Theme.Label("Windows 11 x64 · " + SignedRelease.CurrentRuntime, 11, Theme.Muted));
            Body.Children.Add(Theme.Label("基于 raycalrui/CodexMeter 的独立 Windows 适配。", 11, Theme.Muted));
            Body.Children.Add(Theme.Label("开源协议与原项目说明见程序目录中的 LICENSE 和 UPSTREAM.md。", 11, Theme.Muted));
            Body.Children.Add(Theme.Label("额度与历史保存在本机；启用远程提醒后，所选服务只接收窗口名称和观测到的额度变化。本应用不复制登录凭据、不记录邮箱，也不收集分析数据。", 11, Theme.Muted));
            var links = new WrapPanel();
            links.Children.Add(ActionButton("原项目主页", (s, e) => Open("https://github.com/raycalrui/CodexMeter")));
            var license = ActionButton("查看开源协议", (s, e) => Open(System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "LICENSE"))); license.IsEnabled = !preview; links.Children.Add(license);
            var data = ActionButton("打开本地数据目录", (s, e) => Open(System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "CodexMeter"))); data.IsEnabled = !preview; links.Children.Add(data);
            Body.Children.Add(links);
            Section("Windows 更新设置");
            Body.Children.Add(Theme.Label("发行清单地址（支持 {channel}、{runtime} 占位符）", 11, Theme.Muted));
            var feed = Input(current.UpdateFeed, 4096, "Update.Feed");
            Body.Children.Add(Theme.Label("RSA 签名公钥 JSON", 11, Theme.Muted));
            var key = Input(current.UpdatePublicKey, 4096, "Update.PublicKey"); key.AcceptsReturn = true; key.Height = 64; key.TextWrapping = TextWrapping.Wrap;
            Body.Children.Add(Theme.Label("通道", 11, Theme.Muted)); var channel = Theme.Combo();
            channel.Items.Add(new ComboBoxItem { Content = "Stable", Tag = "stable" }); channel.Items.Add(new ComboBoxItem { Content = "Beta", Tag = "beta" }); channel.SelectedIndex = current.UpdateChannel == "beta" ? 1 : 0; Body.Children.Add(channel);
            var automatic = Toggle("每天自动检查更新", current.UpdateAutomatic); Body.Children.Add(automatic);
            var install = Toggle("空闲时自动下载、安装并重启", current.UpdateAutoInstall); Body.Children.Add(install);
            Body.Children.Add(Theme.Label("仅接受匹配当前运行时的 Windows 签名清单；首次接入需自行提供可信发行地址和公钥。", 11, Theme.Muted));
            var error = Theme.Label("", 11, Theme.Accent); Body.Children.Add(error);
            Body.Children.Add(ActionButton("保存更新设置", (s, e) => {
                try
                {
                    string address = feed.Text.Trim(), publicKey = key.Text.Trim();
                    string selected = (string)((ComboBoxItem)channel.SelectedItem).Tag;
                    if ((address.Length == 0) != (publicKey.Length == 0)) throw new ArgumentException();
                    if (address.Length > 0) { SignedRelease.Https(address.Replace("{channel}", selected).Replace("{runtime}", SignedRelease.CurrentRuntime)); SignedRelease.PublicKey(publicKey); }
                    if ((automatic.IsChecked == true || install.IsChecked == true) && address.Length == 0) throw new ArgumentException();
                    if (install.IsChecked == true && automatic.IsChecked != true) throw new ArgumentException();
                    var next = current.Copy(); next.UpdateFeed = address; next.UpdatePublicKey = publicKey; next.UpdateChannel = selected;
                    next.UpdateAutomatic = automatic.IsChecked == true; next.UpdateAutoInstall = install.IsChecked == true;
                    save(next); updater.Reset(); error.Text = L.T("更新设置已保存。");
                }
                catch (Exception ex) { if (ex is OutOfMemoryException) throw; error.Text = L.T("无法保存；请检查地址、公钥和自动更新选项。安装需同时启用自动检查。"); }
            }));
            Section("检查与安装");
            if (preview)
            {
                var demos = new WrapPanel();
                foreach (var scenario in new[] { new[] { "模拟有更新", "available" }, new[] { "模拟最新版本", "current" }, new[] { "模拟检查失败", "error" } })
                { string selectedDemo = scenario[1]; var button = ActionButton(scenario[0], (s, e) => updater.Simulate(selectedDemo)); System.Windows.Automation.AutomationProperties.SetAutomationId(button, "Update.Demo." + selectedDemo); demos.Children.Add(button); }
                Body.Children.Add(demos);
            }
            var status = Theme.Label("", 12, Theme.Text); Body.Children.Add(status);
            var notes = Theme.Label("", 11, Theme.Muted); Body.Children.Add(notes);
            var check = ActionButton("检查更新", async (s, e) => await updater.Check());
            var download = ActionButton("下载并校验", async (s, e) => await updater.Download());
            var apply = ActionButton("安装并重启", (s, e) => {
                if (L.Confirm(this, "将关闭当前程序并启动已校验的新版本；旧版本目录会保留。是否继续？", "安装更新") == MessageBoxResult.Yes) updater.Install();
            });
            var cancel = ActionButton("取消操作", (s, e) => updater.Cancel());
            foreach (var button in new[] { check, download, apply, cancel }) Body.Children.Add(button);
            System.Windows.Automation.AutomationProperties.SetAutomationId(check, "Update.Check");
            System.Windows.Automation.AutomationProperties.SetAutomationId(download, "Update.Download");
            System.Windows.Automation.AutomationProperties.SetAutomationId(apply, "Update.Install");
            Action refresh = () => {
                status.Text = L.T(updater.Status); notes.Text = updater.Available == null ? "" : updater.Available.Notes;
                check.IsEnabled = !preview && !updater.Busy && updater.Configured;
                download.IsEnabled = !preview && !updater.Busy && updater.Available != null && !updater.Ready;
                apply.IsEnabled = !preview && !updater.Busy && updater.Ready; cancel.IsEnabled = !preview && updater.Busy;
            };
            updater.Changed += refresh; Closed += (s, e) => updater.Changed -= refresh;
            if (!preview) Body.Children.Add(Theme.Label(UpdateWorker.LastResult(), 11, Theme.Muted));
            Actions.Children.Add(ActionButton("完成", (s, e) => Close())); refresh(); FinishLayout();
        }
        private TextBox Input(string text, int maximum, string id)
        {
            var box = new TextBox { Text = text, MaxLength = maximum, Padding = new Thickness(7, 5, 7, 5), Margin = new Thickness(0, 5, 0, 9), Background = Theme.Surface, Foreground = Theme.Text, BorderBrush = Theme.Line };
            System.Windows.Automation.AutomationProperties.SetAutomationId(box, id); Body.Children.Add(box); return box;
        }
        private CheckBox Toggle(string text, bool value) { return new CheckBox { Content = L.T(text), IsChecked = value, Foreground = Theme.Text, Margin = new Thickness(0, 8, 0, 8) }; }
        private static void Open(string value)
        { try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(value) { UseShellExecute = true }); } catch (System.ComponentModel.Win32Exception) { } catch (System.IO.IOException) { } }
    }
}
