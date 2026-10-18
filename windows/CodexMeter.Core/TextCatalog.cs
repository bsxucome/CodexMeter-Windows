using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;

namespace CodexMeter.Core
{
    public static class TextCatalog
    {
        private static readonly KeyValuePair<string, string>[] English = Build();
        private static KeyValuePair<string, string>[] Build()
        {
            return Translations.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(line => line.Split(new[] { '|' }, 2))
                .Select(pair => new KeyValuePair<string, string>(pair[0], pair[1])).OrderByDescending(p => p.Key.Length).ToArray();
        }
        public static string Transform(string value, string language)
        {
            if (value == null || language == "zh-CN") return value;
            if (language == "zh-TW")
            {
                var buffer = new StringBuilder(value.Length * 2 + 2);
                return LCMapStringEx("zh-TW", 0x04000000, value, -1, buffer, buffer.Capacity, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero) > 0 ? buffer.ToString() : value;
            }
            if (language != "en") return value;
            var result = new StringBuilder();
            for (int i = 0; i < value.Length;)
            {
                bool replaced = false;
                foreach (var entry in English)
                    if (i + entry.Key.Length <= value.Length && string.CompareOrdinal(value, i, entry.Key, 0, entry.Key.Length) == 0)
                    { result.Append(entry.Value); i += entry.Key.Length; replaced = true; break; }
                if (!replaced) result.Append(value[i++]);
            }
            return result.ToString();
        }
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
        private static extern int LCMapStringEx(string locale, uint flags, string source, int sourceLength, StringBuilder target,
            int targetLength, IntPtr version, IntPtr reserved, IntPtr sort);
        private const string Translations = @"额度与历史保存在本机；启用远程提醒后，所选服务只接收窗口名称和观测到的额度变化。本应用不复制登录凭据、不记录邮箱，也不收集分析数据。|Quota and history stay on this device. Enabled remote services receive only window names and observed quota changes. This app does not copy login credentials, record emails or collect analytics.
原项目主页|Upstream project
查看开源协议|View license
打开本地数据目录|Open local data folder
提醒五小时额度补充|Five-hour replenishment alerts
提醒每周额度补充|Weekly replenishment alerts
图标地址（可选 HTTPS）|Icon URL (optional HTTPS)
消息优先级：|Message priority: 
预览更新状态…|Preview update states…
模拟有更新|Simulate update available
模拟最新版本|Simulate up to date
模拟检查失败|Simulate check failure
演示更新，不会下载或安装。|Demo update. Download and installation are disabled.
检查失败；演示错误状态。|Check failed (demo state).
连接与账号|Connection and account
任务栏显示|Taskbar display
外观与语言|Appearance and language
弹窗内容|Popover content
历史与数据|History and data
关于与更新|About and updates
关于与检查更新…|About and check for updates…
备份、恢复与账号管理…|Backups, restore and accounts…
远程补充提醒…|Remote replenishment alerts…
远程补充提醒|Remote replenishment alerts
仅在连续观测到额度实际回升时提醒；不会按重置时间猜测。首次读取或切换账号不发送提醒。|Alerts require an observed quota increase between recent samples. The first reading and account changes do not send alerts.
启用此渠道|Enable this channel
服务地址|Server address
Webhook 地址|Webhook URL
发送测试|Send test
清除凭据|Clear credential
正在发送…|Sending…
尚未发送|Not sent yet
发送成功|Sent successfully
发送失败|Delivery failed
服务未确认接收|Service did not confirm receipt
服务限流|Service rate limited
服务暂时不可用|Service temporarily unavailable
凭据被拒绝|Credential rejected
服务重定向被拒绝|Server redirect rejected
请求被拒绝|Request rejected
发送结果未知（超时）|Delivery outcome unknown (timeout)
发送结果未知（网络中断）|Delivery outcome unknown (connection interrupted)
服务响应无效|Invalid service response
渠道配置无效|Invalid channel configuration
已取消|Cancelled
额度已补充|Quota replenished
加密配置无法保存。|Cannot save encrypted configuration.
远程提醒配置无法解密或校验，渠道已暂时停用。|Cannot decrypt or validate remote settings. Channels are temporarily disabled.
预览模式不会发送远程消息。|Preview does not send remote messages.
凭据仅由当前 Windows 用户加密保存，不进入设置、历史备份或日志。测试会发送到当前填写的目标，无需先保存。|Credentials are encrypted for the current Windows user and excluded from settings, history backups and logs. Tests use the entered destination without saving.
这是一条远程提醒测试消息。|This is a remote alert test message.
保存渠道|Save channels
历史与备份|History and backups
历史备份包含全部账号的额度、Token 记录和本地身份盐，不包含登录凭据、远程渠道凭据或偏好设置。|History backups include quota, Token records and identity salt for all accounts. They exclude login data, remote credentials and preferences.
历史备份|History backup
导出历史备份…|Export history backup…
恢复历史备份…|Restore history backup…
历史备份已导出。|History backup exported.
恢复历史备份|Restore history backup
恢复会替换全部本地账号历史，并沿用当前保留期限。开始前会自动备份现有记录；是否继续？|Restore replaces all local account history and keeps the current retention policy. A backup of existing records is created first. Continue?
历史已恢复，恢复前备份已保留。|History restored. The pre-restore backup is retained.
本地账号管理|Local account management
本地账号：|Local accounts: 
本地账号 |Local account 
当前账号|Current account
清除所选账号历史…|Clear selected account history…
清除全部账号历史…|Clear all account history…
清除全部历史|Clear all history
仅清除所选账号的本地历史，是否继续？|Clear local history for the selected account. Continue?
将清除全部账号的额度和 Token 历史；建议先导出备份。是否继续？|Clear quota and Token history for all accounts. Export a backup first if needed. Continue?
所选账号历史已清除。|Selected account history cleared.
全部历史已清除。|All history cleared.
尚无历史数据库。|No history database yet.
预览模式不会访问正式历史。|Preview does not access production history.
正在处理…|Working…
历史操作失败；现有记录与恢复前备份会保留，请检查文件和目录权限。|History operation failed. Existing records and pre-restore backups are retained. Check the file and directory permissions.
Windows 更新设置|Windows update settings
发行清单地址（支持 {channel}、{runtime} 占位符）|Release manifest URL (supports {channel} and {runtime})
RSA 签名公钥 JSON|RSA signing public key JSON
通道|Channel
每天自动检查更新|Check for updates daily
空闲时自动下载、安装并重启|Download, install and restart while idle
仅接受匹配当前运行时的 Windows 签名清单；首次接入需自行提供可信发行地址和公钥。|Accepts signed Windows releases for this runtime. Provide a trusted release URL and public key to configure updates.
保存更新设置|Save update settings
更新设置已保存。|Update settings saved.
无法保存；请检查地址、公钥和自动更新选项。安装需同时启用自动检查。|Cannot save. Check the URL, public key and options. Automatic installation requires automatic checks.
检查与安装|Check and install
检查更新|Check for updates
下载并校验|Download and verify
安装并重启|Install and restart
取消操作|Cancel operation
安装更新|Install update
将关闭当前程序并启动已校验的新版本；旧版本目录会保留。是否继续？|Close this application and start the verified new version. The old version directory will remain. Continue?
预览模式不会访问更新源或安装。|Preview does not access update feeds or install updates.
尚未检查更新。|Updates not checked yet.
尚未配置 Windows 更新源。|No Windows update feed configured.
请先填写 Windows 更新源和签名公钥。|Configure a Windows update feed and signing public key first.
更新配置已保存。|Update configuration saved.
正在检查 Windows 更新…|Checking for Windows updates…
更新配置已改变，请重新检查。|Update configuration changed. Check again.
发现新版本：|New version: 
当前已是此通道的最新版本。|Already up to date for this channel.
检查已取消或超时。|Check cancelled or timed out.
检查失败；请检查更新地址、公钥、通道及网络。|Check failed. Check the feed URL, public key, channel and connection.
正在下载并校验更新…|Downloading and verifying update…
签名与文件校验通过，可安装并重启。|Signature and files verified. Ready to install and restart.
下载已取消或超时。|Download cancelled or timed out.
下载或校验失败；当前版本保持可用。|Download or verification failed. The current version remains available.
正在准备安装…|Preparing installation…
无法启动更新安装；请检查同级目录的写入权限。|Cannot start installation. Check write permission for sibling directories.
基于 raycalrui/CodexMeter 的独立 Windows 适配。|An independent Windows adaptation of raycalrui/CodexMeter.
开源协议与原项目说明见程序目录中的 LICENSE 和 UPSTREAM.md。|See LICENSE and UPSTREAM.md in the application directory.
额度余额|Credit balance
重置机会|Reset opportunities
可用次数：|Available: 
获得于 |Granted at 
到期于 |Expires at 
未提供到期时间|Expiration not provided
部分重置机会未提供可用明细。|Details are unavailable for some reset opportunities.
不限量|Unlimited
有可用余额，具体数量未提供|Credits available; balance not provided
无可用余额|No credits available
余额不可用|Credit balance unavailable
额度详情|Quota details
弹窗区域顺序|Popover section order
区域上移|Move section up
区域下移|Move section down
上下进度条与百分比|Stacked bars and percentages
仅进度条|Progress only
通知与启动|Notifications and startup
启用本地通知|Enable local notifications
低额度提醒|Low quota alerts
消耗偏快提醒|Over-pace alerts
低额度阈值：|Low quota threshold: 
低额度阈值|Low quota threshold
同一窗口同类提醒的冷却时间|Cooldown for the same window and alert type
测试通知|Test notification
登录 Windows 时启动|Start at Windows login
打开 Windows 启动应用设置|Open Windows startup app settings
仅新鲜数据触发提醒；Windows 勿扰或通知设置可能阻止显示。|Only fresh data triggers alerts. Windows notification settings or Do not disturb may suppress delivery.
预览模式不会修改登录启动或发送系统通知。|Preview does not modify startup or send system notifications.
启动项指向其他版本；启用后将更新到当前路径。|The startup entry points to another version. Enabling updates it to this location.
尚未启用登录启动。|Start at login is off.
已注册，但被 Windows 禁用；请在系统启动应用设置中启用。|Registered but disabled by Windows. Enable it in Windows startup app settings.
已注册；Windows 启动批准状态无法确认。|Registered; Windows startup approval status is unknown.
已注册当前版本，登录后自动运行。|This version is registered to run at login.
无法读取 Windows 登录启动状态。|Cannot read Windows startup status.
没有权限更新 Windows 登录启动设置。|Permission denied while updating Windows startup settings.
额度不足提醒|Low quota alert
这是一条测试通知；实际通知显示由 Windows 通知设置控制。|This is a test notification. Display is controlled by Windows notification settings.
当前账号未提供额度窗口。|This account did not provide quota windows.
当前账号的本地观测记录 · F11 全屏|Local observations for this account · F11 for fullscreen
日历周|Calendar week
日历月|Calendar month
全部历史|All history
上一期|Previous
下一期|Next
回到本期|Current period
观测消耗 · |Observed consumption · 
至少 |At least 
按当前节奏预计耗尽于 |Estimated exhaustion at this pace: 
阴影表示超过 30 分钟未记录；虚线为显示趋势，重置基准为 100%。|Shading marks gaps over 30 minutes. Dashes show the trend; known resets start at 100%.
已按额度与 Token 各自的时间范围导出原始记录。|Exported raw records for the independent quota and token time ranges.
按月汇总|Monthly totals
按周汇总|Weekly totals
按日显示|Daily totals
此日期没有记录|No records for this date
未记录区间|Unobserved interval
已记录日期 |Recorded days: 
 条 · 数据库 | · Database: 
每周额度历史|Weekly quota history
标准周额度不可用|Standard weekly quota unavailable
每周额度小图|Weekly quota chart
高级外观|Advanced appearance
此窗口仅使用演示数据；不会修改账号数据或写入历史。|This window uses demo data only. Account data and history remain untouched.
应用后请在设置中保存。|Apply here, then save in Settings.
应用到设置|Apply to Settings
导出外观 JSON…|Export appearance JSON…
导入外观 JSON…|Import appearance JSON…
默认外观|Default appearance
紧凑|Compact
醒目|Bold
双环尺寸|Ring size
外环粗细|Outer stroke
内环粗细|Inner stroke
双环间距|Ring spacing
主字号|Main text size
名称字号|Caption size
图形与数字间距|Symbol spacing
横条宽度|Bar width
显示 Codex 名称|Show Codex caption
时间颜色|Time color
蓝色|Blue
青色|Cyan
紫色|Purple
低额度|Low quota
无重置时间|No reset time
数据过期|Stale data
演示剩余额度|Demo quota remaining
演示剩余时间|Demo time remaining
预览 30 天历史…|Preview 30 days of history…
已导出外观参数，不包含账号数据。|Exported appearance settings without account data.
已载入并限制到安全范围；应用后请保存设置。|Loaded within safe bounds. Apply, then save Settings.
外观文件无效或无法读取，请选择本应用导出的 JSON。|Invalid or unreadable appearance file. Select JSON exported by this app.
查看额度|View quota
立即刷新|Refresh now
历史记录|History
设置|Settings
退出|Quit
刷新|Refresh
正在刷新|Refreshing
正在连接…|Connecting…
正在连接|Connecting
正在检查账号|Checking account
正在读取额度…|Reading quota…
未知套餐|Unknown plan
Codex 账号|Codex account
预览模式 · 演示数据，不读取或写入本地历史。|Preview · Demo data. Local history is not accessed.
预览模式 · 演示数据|Preview · Demo data
当前账号的本地观测记录 · 不跨重置周期或离线空档连接曲线|Local observations for the current account · Gaps and resets break the line
数据可能已过期。 |Data may be stale. 
尚无可用额度|No quota available
请先在本机 Codex CLI 中完成登录。可在设置中指定安装位置。|Sign in with the local Codex CLI. Select its location in Settings if needed.
节奏未知|Pace unknown
节奏正常|On pace
消耗偏快|Ahead of pace
剩余额度历史图|Quota history chart
每日 Token 历史图|Daily token chart
剩余额度|Quota left
剩余时间进度条|Time remaining bar
剩余时间|Time left
重置于 |Resets at 
重置时间不可用|Reset time unavailable
等待额度重置|Waiting for quota reset
Token 活动与图表|Token activity and chart
Token 活动可能已过期。|Token activity may be stale.
Token 活动查询超时，将稍后重试。|Token query timed out. It will retry later.
Token 活动暂时无法读取。|Token activity is temporarily unavailable.
当前 CLI 或账号不支持 Token 活动。|This CLI or account does not support token activity.
服务未返回 Token 活动。|The service returned no token activity.
Token 活动|Token activity
累计 Tokens|Lifetime tokens
单日峰值|Peak daily tokens
当前连续天数|Current streak (days)
最长连续天数|Longest streak (days)
最长任务（秒）|Longest turn (seconds)
最近 30 天 · 按服务日期|Last 30 days · Service dates
查看历史记录|View history
等待更新|Waiting for update
更新于 |Updated at 
Codex CLI 路径|Codex CLI path
选择文件…|Browse…
选择 Codex CLI|Select Codex CLI
Codex 启动入口|Codex entry point
所有文件|All files
留空自动查找。登录由 Codex CLI 管理，本应用不读取登录凭据。|Leave blank to auto-detect. Codex CLI manages sign-in; this app does not read credentials.
在任务栏常驻显示双环和百分比|Show the quota indicator on the taskbar
点击展开额度，拖动调整位置。右键可重置位置。|Click to view quota. Drag to move. Right-click for placement options.
任务栏位置|Taskbar position
紧靠 Windows 开始按钮左侧|Next to Start, on the left
紧靠 Windows 开始按钮右侧|Next to Start, on the right
自定义拖动位置|Custom dragged position
自动靠右|Automatic, on the right
重置任务栏位置|Reset taskbar position
任务栏显示样式|Taskbar style
双环与百分比|Rings and percentage
双横条与百分比|Dual bars and percentage
上下排列百分比|Stacked percentages
仅百分比|Percentage
进度条与百分比|Progress bar and percentage
任务栏与托盘代表的额度窗口|Quota represented by taskbar and tray
默认（优先标准周额度）|Default (prefer standard weekly quota)
保留之前选择（当前不可用）|Keep previous choice (currently unavailable)
外观|Appearance
跟随系统|System
浅色|Light
深色|Dark
语言|Language
简体中文|简体中文
繁體中文|繁體中文
弹窗内容|Popover content
消耗节奏提示|Pacing status
重置倒计时与日期|Reset countdown and date
额度窗口显示与顺序|Visible quota windows and order
上移|Move up
下移|Move down
在本机记录额度历史和 Token 活动|Record quota and token history on this device
只保存额度、日期和加盐账号标识，不保存邮箱或登录凭据。关闭后停止新增，已有记录可在历史窗口清除。|Stores quota, dates and salted account identifiers, without email or credentials. Turning this off pauses recording; existing records can be cleared in History.
历史保留期限（适用于全部本地账号）|History retention (all local accounts)
永久|Forever
保存设置|Save settings
文件不存在，请重新选择。|File not found. Select another file.
无法识别所选 CLI，请选择 codex.exe 或 npm 的 codex.cmd。|Unrecognized CLI. Select codex.exe or the npm codex.cmd entry.
设置无法保存，请检查本地数据目录。|Cannot save settings. Check the local data directory.
没有权限保存设置，请检查本地数据目录。|No permission to save settings. Check the local data directory.
缩短保留期限会删除全部本地账号中超过期限的历史记录，是否继续？|Shortening retention deletes older history for all local accounts. Continue?
历史保留期限|History retention
当前重置周期|Current reset cycle
最近 7 天|Last 7 days
最近 14 天|Last 14 days
最近 30 天|Last 30 days
最近 90 天|Last 90 days
最近 365 天|Last 365 days
历史时间范围|History time range
历史额度窗口|History quota window
导出 CSV…|Export CSV…
清除当前账号记录…|Clear current account history…
点与线仅代表实际观测；重置、额度回升或超过 30 分钟的空档会断开。|Only observed samples are drawn. Resets, replenishment and gaps over 30 minutes break the line.
每日 Token 活动 · 已记录 |Daily token activity · Recorded 
按服务返回的日期保存；没有返回的日期留空，不推算为零。|Dates come from the service. Missing dates are left empty, without assuming zero.
演示数据 · 导出和清除已禁用|Demo data · Export and clear are disabled
历史记录已关闭或数据库不可用。|History is off or the database is unavailable.
连接账号后显示其历史记录。|Connect an account to view its history.
额度采样 |Quota samples: 
 条 · Token 日期 | · Token dates: 
 天 · 数据库 | · Database: 
 · 当前已暂停新增记录| · Recording is paused
历史记录无法读取，实时额度仍可使用。|Cannot read history. Live quota remains available.
没有读取历史记录的权限。|No permission to read history.
CSV 文件|CSV file
已导出当前时间范围内的全部额度窗口和 Token 记录。|Exported all quota windows and token records in the selected time range.
导出失败，请检查目标文件是否可写。|Export failed. Check that the destination is writable.
没有写入目标文件的权限。|No permission to write the destination file.
删除当前账号在本机保存的额度与 Token 历史？其他账号记录会保留。|Delete this account's local quota and token history? Other accounts will be kept.
清除历史记录|Clear history
清除失败，请检查本地数据目录。|Cannot clear history. Check the local data directory.
尚无窗口|No window
尚无记录|No records yet
悬停查看采样日期和数值；空白表示没有观测数据。|Hover to inspect samples. Blank areas have no observations.
（服务日期）| (service date)
CodexMeter 任务栏额度|CodexMeter taskbar quota
等待额度数据|Waiting for quota data
点击展开；拖动调整位置|Click to open; drag to move
，剩余额度 |, quota left 
 剩余 | remaining 
（已过期）| (stale)
更新 |Updated 
尚未登录 Codex。请在终端运行 codex login 后点击刷新。|Not signed in to Codex. Run codex login in a terminal, then refresh.
尚未登录|Not signed in
Codex 连接已中断，将自动重试。|Codex disconnected. Retrying automatically.
此账号未返回可用额度。请确认 CLI 登录方式与账号支持额度查询。|No quota returned. Check that the CLI sign-in method and account support quota queries.
请求超时，将自动重试；也可以手动刷新。|Request timed out. Retrying automatically; manual refresh is also available.
Codex 无法读取额度，请确认登录状态并更新 CLI 后重试。|Codex could not read quota. Check sign-in and update the CLI, then retry.
Codex 无法访问用户主目录，请在正常 Windows 登录会话中运行应用。|Codex cannot access the home directory. Run the app in a normal Windows login session.
无法连接 Codex App Server，请检查 CLI 路径后重试。|Cannot connect to Codex App Server. Check the CLI path and retry.
本地历史记录未能保存，实时额度仍可使用。|Cannot save history. Live quota remains available.
历史记录保留期限未能应用，请检查本地数据目录。|Cannot apply history retention. Check the local data directory.
没有保存本地历史记录的权限，实时额度仍可使用。|No permission to save history. Live quota remains available.
本地历史数据库无法打开，实时额度仍可使用。|Cannot open the history database. Live quota remains available.
历史账号标识无法读取，实时额度仍可使用。|Cannot read the history account identifier. Live quota remains available.
没有访问本地历史数据库的权限，实时额度仍可使用。|No permission to access history. Live quota remains available.
系统 SQLite 不可用，实时额度仍可使用。|System SQLite is unavailable. Live quota remains available.
系统 SQLite 版本不受支持，实时额度仍可使用。|System SQLite is unsupported. Live quota remains available.
位置已临时调整，但没有保存设置的权限。|Position changed for this session. No permission to save it.
位置已临时调整，但设置未能保存。|Position changed for this session, but could not be saved.
设置文件无法读取，已使用默认设置。保存设置可修复此问题。|Cannot read settings. Defaults are in use; save settings to repair the file.
CodexMeter 无法启动。请确认解压了完整文件夹，并检查本地设置目录是否可访问。|CodexMeter cannot start. Extract the complete folder and check access to the local settings directory.
每周|Weekly
额度窗口|Quota window
 小时后重置| h until reset
 分钟后重置| min until reset
 小时| h
 分钟| min
 天| days";
    }
}
