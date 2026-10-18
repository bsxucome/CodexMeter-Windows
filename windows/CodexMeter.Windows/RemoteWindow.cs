using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using CodexMeter.Core;

namespace CodexMeter.Windows
{
    internal sealed class RemoteWindow : CompactWindow
    {
        private readonly CancellationTokenSource lifetime = new CancellationTokenSource();
        private readonly Dictionary<string, TextBlock> status = new Dictionary<string, TextBlock>();
        private readonly Dictionary<string, CheckBox> enabled = new Dictionary<string, CheckBox>();
        private readonly Dictionary<string, TextBox> endpoints = new Dictionary<string, TextBox>(), recipients = new Dictionary<string, TextBox>();
        private readonly Dictionary<string, PasswordBox> secrets = new Dictionary<string, PasswordBox>();
        private readonly Dictionary<string, CheckBox> fiveHour = new Dictionary<string, CheckBox>(), weekly = new Dictionary<string, CheckBox>();
        private TextBox barkIcon;
        private Slider gotifyPriority;
        public RemoteWindow(string theme, bool preview, RemoteProfile current, Action<RemoteProfile> save,
            Func<RemoteChannel, CancellationToken, Task<DeliveryResult>> test) : base("远程补充提醒", theme, preview)
        {
            Body.Children.Add(Theme.Label("仅在连续观测到额度实际回升时提醒；不会按重置时间猜测。首次读取或切换账号不发送提醒。", 11, Theme.Muted));
            foreach (var channel in current.Channels)
            {
                string kind = channel.Kind; Section(kind);
                var toggle = new CheckBox { Content = L.T("启用此渠道"), IsChecked = channel.Enabled, Foreground = Theme.Text, Margin = new Thickness(0, 4, 0, 8) };
                System.Windows.Automation.AutomationProperties.SetAutomationId(toggle, "Remote.Enabled." + kind); enabled[kind] = toggle; Body.Children.Add(toggle);
                fiveHour[kind] = WindowToggle("提醒五小时额度补充", channel.FiveHour, "Remote.FiveHour." + kind);
                weekly[kind] = WindowToggle("提醒每周额度补充", channel.Weekly, "Remote.Weekly." + kind);
                if (kind == "Bark" || kind == "Gotify")
                { Body.Children.Add(Theme.Label("服务地址", 11, Theme.Muted)); endpoints[kind] = Input(channel.Endpoint, false); }
                Body.Children.Add(Theme.Label(kind == "Discord" ? "Webhook 地址" : kind == "Telegram" ? "Bot Token" : kind == "Bark" ? "Device Key" : "Application Token", 11, Theme.Muted));
                var secret = new PasswordBox { Password = channel.Secret, MaxLength = 2048, Padding = new Thickness(8, 6, 8, 6), Margin = new Thickness(0, 5, 0, 8), Background = Theme.Surface, Foreground = Theme.Text, BorderBrush = Theme.Line };
                System.Windows.Automation.AutomationProperties.SetAutomationId(secret, "Remote.Secret." + kind); secrets[kind] = secret; Body.Children.Add(secret);
                if (kind == "Telegram") { Body.Children.Add(Theme.Label("Chat ID", 11, Theme.Muted)); recipients[kind] = Input(channel.Recipient, true); }
                if (kind == "Bark") { Body.Children.Add(Theme.Label("图标地址（可选 HTTPS）", 11, Theme.Muted)); barkIcon = Input(channel.IconUrl, false); }
                if (kind == "Gotify")
                {
                    var priorityLabel = Theme.Label("", 11, Theme.Muted); Body.Children.Add(priorityLabel);
                    gotifyPriority = new Slider { Minimum = 0, Maximum = 10, Value = channel.Priority, TickFrequency = 1, IsSnapToTickEnabled = true, Margin = new Thickness(0, 5, 0, 10) };
                    System.Windows.Automation.AutomationProperties.SetAutomationId(gotifyPriority, "Remote.Priority.Gotify");
                    gotifyPriority.ValueChanged += (s, e) => priorityLabel.Text = L.T("消息优先级：") + gotifyPriority.Value.ToString("0");
                    priorityLabel.Text = L.T("消息优先级：") + channel.Priority; Body.Children.Add(gotifyPriority);
                }
                var buttons = new StackPanel { Orientation = Orientation.Horizontal };
                var testButton = ActionButton("发送测试", async (s, e) => {
                    var button = (Button)s; button.IsEnabled = false; status[kind].Text = L.T("正在发送…");
                    try
                    {
                        var value = Read(kind); RemoteDelivery.Validate(value);
                        var result = await test(value, lifetime.Token);
                        if (!lifetime.IsCancellationRequested) status[kind].Text = L.T(result.Status) + " · " + result.Attempts;
                    }
                    catch (ArgumentException) { status[kind].Text = L.T("渠道配置无效"); }
                    catch (OperationCanceledException) { }
                    finally { if (!lifetime.IsCancellationRequested) button.IsEnabled = !preview; }
                });
                testButton.IsEnabled = !preview; System.Windows.Automation.AutomationProperties.SetAutomationId(testButton, "Remote.Test." + kind); buttons.Children.Add(testButton);
                buttons.Children.Add(ActionButton("清除凭据", (s, e) => secret.Clear())); Body.Children.Add(buttons);
                var label = Theme.Label(preview ? "预览模式不会发送远程消息。" : "尚未发送", 11, Theme.Muted); label.Margin = new Thickness(0, 6, 0, 0); status[kind] = label; Body.Children.Add(label);
            }
            Body.Children.Add(Theme.Label("凭据仅由当前 Windows 用户加密保存，不进入设置、历史备份或日志。测试会发送到当前填写的目标，无需先保存。", 11, Theme.Muted));
            var error = Theme.Label("", 11, Theme.Accent); Actions.Children.Add(error);
            Actions.Children.Add(ActionButton("保存渠道", (s, e) => {
                try
                {
                    var profile = new RemoteProfile();
                    foreach (var channel in profile.Channels)
                    {
                        var value = Read(channel.Kind); channel.Enabled = value.Enabled; channel.Endpoint = value.Endpoint; channel.Secret = value.Secret; channel.Recipient = value.Recipient;
                        channel.FiveHour = value.FiveHour; channel.Weekly = value.Weekly; channel.IconUrl = value.IconUrl; channel.Priority = value.Priority;
                        if (channel.Enabled) RemoteDelivery.Validate(channel);
                    }
                    save(profile); Close();
                }
                catch (ArgumentException) { error.Text = L.T("渠道配置无效"); }
                catch (System.IO.IOException) { error.Text = L.T("加密配置无法保存。"); }
                catch (UnauthorizedAccessException) { error.Text = L.T("加密配置无法保存。"); }
            }));
            Closed += (s, e) => { lifetime.Cancel(); foreach (var secret in secrets.Values) secret.Clear(); lifetime.Dispose(); };
            FinishLayout();
        }
        private TextBox Input(string value, bool recipient)
        {
            var box = new TextBox { Text = value, MaxLength = recipient ? 128 : 2048, FontSize = 12, Padding = new Thickness(8, 6, 8, 6), Margin = new Thickness(0, 5, 0, 8), Background = Theme.Surface, Foreground = Theme.Text, BorderBrush = Theme.Line };
            Body.Children.Add(box); return box;
        }
        private RemoteChannel Read(string kind)
        { return new RemoteChannel { Kind = kind, Enabled = enabled[kind].IsChecked == true, Endpoint = endpoints.ContainsKey(kind) ? endpoints[kind].Text.Trim() : "", Secret = secrets[kind].Password.Trim(), Recipient = recipients.ContainsKey(kind) ? recipients[kind].Text.Trim() : "",
            FiveHour = fiveHour[kind].IsChecked == true, Weekly = weekly[kind].IsChecked == true, IconUrl = kind == "Bark" ? barkIcon.Text.Trim() : "", Priority = kind == "Gotify" ? (int)gotifyPriority.Value : 5 }; }
        private CheckBox WindowToggle(string text, bool value, string id)
        { var toggle = new CheckBox { Content = L.T(text), IsChecked = value, Foreground = Theme.Text, Margin = new Thickness(0, 2, 0, 8) }; System.Windows.Automation.AutomationProperties.SetAutomationId(toggle, id); Body.Children.Add(toggle); return toggle; }
        public void SetStatus(string kind, string value) { TextBlock label; if (status.TryGetValue(kind, out label)) label.Text = L.T(value); }
    }
}
