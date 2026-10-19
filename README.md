# CodexMeter for Windows

Windows 任务栏与托盘客户端，参考 [raycalrui/CodexMeter](https://github.com/raycalrui/CodexMeter) 的额度显示规则与本地 App Server 架构。v0.7.0 补齐分窗口远程提醒、开发预览、恢复中断保护和长历史优化，并加入公开仓库与 Release 自动化。独立社区移植，非 OpenAI 官方产品。

功能、运行和构建说明见 [windows/README.md](windows/README.md)。
分阶段补齐原项目功能的状态与后续验收内容见 [功能对照清单](windows/PARITY.md)。

<img src="docs/images/main-light.png" width="300" alt="主面板浅色演示" /> <img src="docs/images/settings-dark.png" width="300" alt="设置页深色演示" />

截图使用独立演示数据；任务栏实际停靠需要在正常 Windows 桌面验收。

公开仓库：[bsxucome/CodexMeter-Windows](https://github.com/bsxucome/CodexMeter-Windows)。本地源码归档、浏览器授权发布脚本与 CI 已提供；具体推送及托管构建验收状态见 [VERIFICATION.md](windows/VERIFICATION.md)。操作见 [GitHub 发布说明](windows/RELEASES.md)，正常桌面待验收项目见 [ACCEPTANCE.md](windows/ACCEPTANCE.md)。

发布脚本已处理沙箱账号创建仓库后，普通用户运行时的 Git 所有权检查；只为本次执行信任该项目路径。Windows PowerShell 5 的未登录提示、仓库不存在和推送进度也按退出码处理，完整流程已有隔离测试。

构建排查可双击 `Collect-CI-Logs.cmd`。维护者在此工作目录提交修正后，可双击 `Sync-CI-Fix.cmd`：同步已提交的 main，等待对应提交的 Windows 构建结束，并把结果保存到 `windows/artifacts/ci-logs/latest-result.txt`。同步入口仅用于此仓库，校验 origin，保留现有登录且不强推。遇到 Schannel 握手失败时，会临时使用 OpenSSL 和 HTTP/1.1 重试一次，保留证书验证，不修改全局 Git 设置；认证、证书或分支冲突错误会停止。

[Windows 自动构建已通过](https://github.com/bsxucome/CodexMeter-Windows/actions/runs/39182365683)，对应修复提交 `0256625`。Framework 和 .NET 10 构建、测试及构建产物上传均完成，可在该运行页面的 Artifacts 中获取构建包。详细证据见 [验证记录](windows/VERIFICATION.md)。

正式版本 [0.7.0 已发布](https://github.com/bsxucome/CodexMeter-Windows/releases/tag/windows-v0.7.0)，提供 .NET 10 自包含包、Framework 兼容包及各自的 SHA-256 文件。

新版本可直接在 [Actions → Windows builds](https://github.com/bsxucome/CodexMeter-Windows/actions/workflows/windows.yml) 点击 **Run workflow**，选择 `main` 和 `operation=release`。同一次运行会校验版本、构建测试两种运行时、打包、创建标签并发布 Release；完成后在 Summary 查看发行链接。`operation=build` 仅构建打包。发布前需更新代码中的版本号；已发布版本保持不变。本地 `Publish-Release.cmd` 入口也保留。操作细节见 [发行说明](windows/RELEASES.md)。

此工作目录包含新增的 Windows 客户端，**不是完整上游克隆**。当前环境无法通过 Git 下载仓库；macOS 文件未复制或修改。来源及同步限制见 [UPSTREAM.md](UPSTREAM.md)。
