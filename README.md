# CodexMeter for Windows

Windows 任务栏与托盘客户端，参考 [raycalrui/CodexMeter](https://github.com/raycalrui/CodexMeter) 的额度显示规则与本地 App Server 架构。v0.7.0 补齐分窗口远程提醒、开发预览、恢复中断保护和长历史优化，并加入公开仓库与 Release 自动化。独立社区移植，非 OpenAI 官方产品。

功能、运行和构建说明见 [windows/README.md](windows/README.md)。
分阶段补齐原项目功能的状态与后续验收内容见 [功能对照清单](windows/PARITY.md)。

<img src="docs/images/main-light.png" width="300" alt="主面板浅色演示" /> <img src="docs/images/settings-dark.png" width="300" alt="设置页深色演示" />

截图使用独立演示数据；任务栏实际停靠需要在正常 Windows 桌面验收。

公开仓库：[bsxucome/CodexMeter-Windows](https://github.com/bsxucome/CodexMeter-Windows)。本地源码归档、浏览器授权发布脚本与 CI 已提供；具体推送及托管构建验收状态见 [VERIFICATION.md](windows/VERIFICATION.md)。操作见 [GitHub 发布说明](windows/RELEASES.md)，正常桌面待验收项目见 [ACCEPTANCE.md](windows/ACCEPTANCE.md)。

发布脚本已处理沙箱账号创建仓库后，普通用户运行时的 Git 所有权检查；只为本次执行信任该项目路径。Windows PowerShell 5 的未登录提示、仓库不存在和推送进度也按退出码处理，完整流程已有隔离测试。

此工作目录包含新增的 Windows 客户端，**不是完整上游克隆**。当前环境无法通过 Git 下载仓库；macOS 文件未复制或修改。来源及同步限制见 [UPSTREAM.md](UPSTREAM.md)。
