# 正常 Windows 会话验收

当前执行环境和用户桌面同属 Session 1，但隔离桌面中看不到 Explorer 任务栏；CLI 和 DPAPI 也无法访问正常用户配置。因此自动测试、真实子进程交接与屏幕操作分别记录，不能互相代替。这里的检查尚未全部完成。

在普通 Windows PowerShell 中进入项目目录，运行：

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File windows/scripts/build.ps1 -Runtime Framework
powershell -NoProfile -ExecutionPolicy Bypass -File windows/scripts/update-process-smoke.ps1
powershell -NoProfile -ExecutionPolicy Bypass -File windows/scripts/indicator-smoke.ps1
powershell -NoProfile -ExecutionPolicy Bypass -File windows/scripts/release-smoke.ps1
```

脚本只处理各自的测试数据和进程，保留已有用户实例。核心测试在正常用户配置可用时还会验证 DPAPI 的加密文件与解密读回。更新进程测试使用 `.fixture` 标记和独立 GUID 目录，不读取正式设置、历史或渠道，不注册登录启动，不访问线上更新源。

需要验证真实账号时，明确选择 CLI 路径运行以下只读探测。输出只有窗口数量、过期状态和本地编写的错误摘要；不保存账号邮箱或认证响应：

```powershell
windows/artifacts/CodexMeter-framework-x64/CodexMeter.Tests.exe --probe "C:\PATH\TO\codex.cmd"
```

| 场景 | 验收方法 | 当前结果 |
| --- | --- | --- |
| 圆环与数字的全部点击区域 | 连续开关详情，测试圆心、边角和数字间隙，拖动后再点击 | 原生 HWND 命中和预览交互通过；物理鼠标待验收 |
| 开始按钮左右、自动隐藏 | 改变任务栏对齐与停靠，打开／收起自动隐藏任务栏 | 几何规则通过；真实 Explorer 待验收 |
| 多屏与缩放 | 100%／150%／200% 屏幕切换、主副屏全屏与恢复 | 几何／DPI 输入通过；真实混合屏幕待验收 |
| Explorer 重启、休眠与解锁 | 在可控会话执行后检查圆环重新定位 | 恢复事件已接入；真实系统事件待验收 |
| 真实账号 | 登录后刷新、切换账号、模拟 CLI 断连再恢复 | 隔离 RPC 测试通过；本环境主目录访问失败 |
| 远程提醒与 DPAPI | 正常会话保存渠道，重开确认，再用“发送测试”检查目标服务 | 请求、取消、窗口选择与脱敏通过；实际投递及正常用户加密待验收 |
| 本地通知与登录启动 | 设置内主动测试通知、保存登录启动开关，再按系统设置复核 | 规则与注册项夹具通过；实际系统交付待验收 |
| 备份与恢复 | 导出后另存；确认恢复前备份可读，账号盐和记录恢复正确 | 真实 SQLite、多账号、失败注入和子进程中断恢复通过 |
| 更新安装／启动失败 | 运行进程脚本；托管发行源上线后再做一次真实下载更新 | 真实辅助进程／WPF 启动／旧进程回退及篡改拒绝通过；线上源待验收 |
| 长历史与语言 | 浏览大历史、缩放图表、检查三语言长文本与窄窗口 | 20 万点显示缩减、原始悬停命中、1 万条数据库查询及预览通过 |

不要为取得一张“全部通过”的表而关闭用户实例、修改真实账号数据、发送未配置的消息或写入启动批准状态。完成对应正常桌面检查后，再更新本表与 VERIFICATION.md。
