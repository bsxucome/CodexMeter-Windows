# CodexMeter for Windows

**把 Codex 额度、重置时间与使用趋势，放到 Windows 任务栏上。**

[简体中文](README.md) · [English](README.en.md)

[![下载最新版](https://img.shields.io/github/v/release/bsxucome/CodexMeter-for-Windows?label=下载最新版)](https://github.com/bsxucome/CodexMeter-for-Windows/releases/latest)
[![Windows 构建](https://github.com/bsxucome/CodexMeter-for-Windows/actions/workflows/windows.yml/badge.svg)](https://github.com/bsxucome/CodexMeter-for-Windows/actions/workflows/windows.yml)
[![MIT 开源许可](https://img.shields.io/badge/开源许可-MIT-blue)](LICENSE)

CodexMeter for Windows 是一款面向 **Windows 11 x64** 的 Codex 额度监控工具。任务栏双环与百分比让你随时了解剩余额度和时间；点击即可查看详细额度、历史曲线和 Token 活动，帮助安排使用节奏。

项目参考 [raycalrui/CodexMeter](https://github.com/raycalrui/CodexMeter) 独立实现 Windows 版本，默认使用简体中文，同时支持繁体中文与英文。

## 界面预览

<p>
  <img src="docs/images/main-light.png" width="300" alt="浅色主面板：额度、剩余时间与使用记录" />
  <img src="docs/images/settings-dark.png" width="300" alt="深色设置页：任务栏显示、内容与语言设置" />
</p>

*截图使用演示数据，展示主面板与设置页；实际额度以你的 Codex 账号返回的数据为准。*

## 主要功能

| 功能 | 你可以做什么 |
| --- | --- |
| **任务栏常驻显示** | 用双环与百分比查看剩余额度和时间，点击展开详情；支持靠近开始按钮停靠和拖动调整位置。 |
| **多窗口额度监控** | 查看服务提供的五小时、每周等额度窗口，以及重置倒计时、准确重置时间和消耗节奏。 |
| **额度历史与趋势** | 按周期、自然周或自然月浏览额度曲线，查看观测消耗；数据充分时给出有条件的耗尽时间估计。 |
| **Token 活动统计** | 查看累计用量、每日活动、单日峰值与连续使用天数；支持独立时间范围和长期汇总。 |
| **外观与内容定制** | 切换双环、横条、上下进度条、纯百分比或纯进度条；调整尺寸、颜色、字号及详情区域顺序。 |
| **本地与远程提醒** | 配置低额度、消耗偏快通知；可选 Bark、Gotify、Telegram、Discord 额度补充提醒。 |
| **历史数据管理** | 按账号隔离本地历史，设置保留期限，导出 CSV，备份、恢复或清除记录。 |
| **日常使用体验** | 系统托盘入口、登录时启动、明暗主题，以及简体中文、繁体中文和英文界面。 |

设置页隐藏右侧滚动条，保留滚轮与键盘翻页；鼠标停在排序列表内也能继续滚动页面，较长列表可先滚动内部内容，到边界后接续页面滚动。

高级外观与设置页采用相同的紧凑窗口，隐藏滚动条，并固定实时预览和底部操作。参数按图形、文字与颜色分组，支持中英文界面。

设置及管理页面使用柔和灰底、圆角输入框与列表、淡色选中行和醒目的底部操作按钮，浅色与深色主题保持一致；排序列表随内容调整高度，减少空白区域。

余额、重置机会和 Token 活动取决于账号与服务接口是否提供。缺失的数据会标为不可用或省略，不会被当作零；远程提醒需要手动配置和启用。

## 下载与快速上手

前往 **[下载最新版](https://github.com/bsxucome/CodexMeter-for-Windows/releases/latest)**，选择适合你的便携包：

| 下载包 | 适用情况 |
| --- | --- |
| `dotnet10-x64.zip` | **推荐**：自带 .NET 运行时，无需单独安装 .NET。 |
| `framework-x64.zip` | 体积更小，需要 .NET Framework 4.8 或更新版本。 |

1. 安装 Windows 原生 Codex CLI，并在终端运行 `codex login` 完成登录。
2. 下载并完整解压便携包，运行 `CodexMeter.exe`。
3. 在任务栏查看双环，点击打开详情；首次未找到 CLI 时，在设置中指定其路径。
4. 按需要选择额度窗口、显示样式、语言和提醒方式。

每个下载包均附有 SHA-256 校验文件。升级时先退出旧版，再将新版完整解压到独立目录；偏好设置与历史记录保存在当前用户的本地应用数据目录。

更多配置、快捷操作和源码构建方法见 **[使用与构建指南](windows/README.md)**。

## 数据与隐私

- 通过本地 Codex App Server 读取账号及额度信息，登录由 Codex CLI 管理。
- 不读取认证文件，不保存邮箱、密码或访问令牌，不发送遥测。
- 历史记录默认保存在本机，可暂停记录、导出、备份或删除；账号使用本地加盐标识分区。
- 远程提醒仅在主动启用后向所选服务发送消息；渠道凭据使用当前 Windows 用户的 DPAPI 加密保存。

## 使用范围

当前以 **Windows 11 x64、主屏横向任务栏** 为目标。任务栏显示采用独立透明浮层；多屏混合缩放、自动隐藏及部分系统交互仍需持续实机验证。暂不支持 ARM64 或 WSL CLI。

应用已提供可配置更新器，但正式更新源与可信公钥尚未配置；当前请从发行页手动下载。便携包尚未进行代码签名。详细支持状态见 [功能对照](windows/PARITY.md) 与 [实机验收清单](windows/ACCEPTANCE.md)。

## 文档与反馈

- [使用与构建指南](windows/README.md)：安装、配置、数据存储与本地构建。
- [发行维护指南](windows/RELEASES.md)：自动构建、打包、发版与更新源配置。
- [提交问题或建议](https://github.com/bsxucome/CodexMeter-for-Windows/issues/new/choose)：欢迎中文或英文反馈。
- [参与贡献](CONTRIBUTING.md) · [验证记录](windows/VERIFICATION.md) · [上游来源](UPSTREAM.md)。

## 开源与致谢

本项目采用 [MIT 许可](LICENSE)，感谢 [raycalrui/CodexMeter](https://github.com/raycalrui/CodexMeter) 的开源工作，保留原作者版权声明。项目为独立社区实现，非 OpenAI 官方产品；Windows 版本的具体实现范围与上游来源见 [UPSTREAM.md](UPSTREAM.md)。
