# Windows 发行与更新接入

## 0.7.6 项目主页与 GitHub 更新源

“关于与更新”的项目主页、Windows 下载入口指向 bsxucome/CodexMeter-for-Windows，上游链接单独保留。默认更新地址与 Actions 的 windows-update-feed 资产同步机制对齐；保留自定义源、通道及公钥，不自动启用检查或安装。见 [0.7.6 发行说明](releases/0.7.6.md)。远程构建与新版本发布须另行确认。

## 0.7.5 任务栏与首次弹窗修复

功能窗口统一完整项目名称；修复重复显示双环、任务栏测量回退跳动及首次弹窗定位，打开的详情跟随双环位置变化。见 [0.7.5 发行说明](releases/0.7.5.md)。版本与原有数据、文件名及更新标识保持兼容，已有标签不移动。新的远程构建与 Release 仍需成功运行后确认。

0.7.4 已发布，正常用户日志确认标签运行 39525289007 成功，发布提交为 584ee82415783a2fc42937856bcbdef05f86e56f，四项附件齐全。以下保留本地准备记录。

## 0.7.4 设置页面样式

设置、高级外观及相关管理窗口统一柔和灰底、圆角列表／输入框、淡色选中态与强调色底栏按钮，排序列表按内容适配高度。见 [0.7.4 发行说明](releases/0.7.4.md)。本地准备不代表已发版，正式下载以 Release 实际附件为准。

0.7.3 已由 [标签流水线 39454299274](https://github.com/bsxucome/CodexMeter-for-Windows/actions/runs/39454299274) 发布，提交为 ead67d6195df2e39b7f681b6fe0e14c64be632bf；两个 ZIP 和对应 SHA-256 文件均由正常用户会话的 GitHub API 回读确认已上传。以下保留此前的准备记录。

## 0.7.3 发布准备

0.7.2 的 main 构建成功，但标签发版遇到 Windows PowerShell 中文内联编码错误及 UIA 就绪超时，Release 未发布。0.7.3 改用带 BOM 的独立签名脚本，并把两种运行时的无密钥／测试签名路径加入 main 回归；界面测试使用有界就绪等待。见 [0.7.3 发行说明](releases/0.7.3.md)。保留旧标签，新的标签仅在 main 构建成功后由发布入口创建。

## 0.7.2 发布准备

0.7.1 的远程 Framework 测试遇到 npm 夹具路径长度限制，尚未确认该版本的 Release。0.7.2 保留设置体验修复并修正测试路径，增加与云端等长目录回归；见 [0.7.2 发行说明](releases/0.7.2.md)。原 windows-v0.7.1 标签保留，后续使用 windows-v0.7.2 触发完整构建与发布，不移动或覆盖已有标签。

## 0.7.1 发布准备

0.7.1 包含设置页滚轮修复、高级外观紧凑布局与滚动条隐藏；具体变化见 [0.7.1 发行说明](releases/0.7.1.md)。源码、SDK 与清单版本已统一。推送 `windows-v0.7.1` 标签会由现有 Actions 构建、测试并发布两个运行时的便携包和 SHA-256 文件；普通 main 推送仍只构建。只有 Actions 成功且 Release 附件齐全后，才视为已发布。

## 项目页面与仓库改名

项目展示名称为 **CodexMeter for Windows**，目标公开地址为 `bsxucome/CodexMeter-for-Windows`。首页默认中文，通过 `English` 入口切换英文介绍。GitHub 简介、首页链接和主题标签保存在 `.github/repository-profile.json`。

维护者将准备好的改动提交后，在正常 Windows 会话双击根目录 `Update-GitHub-Page.cmd`。它先检查精确的旧／新仓库地址、main、干净的已跟踪文件和 GitHub 管理权限；在 GitHub 确认改名后才更新本地 origin，使用带 TLS 兼容重试的入口推送已有提交，再同步简介、保留已有主题标签，并用本地中文发行说明更新既有 Release 的标题和正文。远程状态会回读核验，结果保存在 `windows/artifacts/github-page/latest-result.json`。

此入口不会创建新仓库、版本标签或 Release，不会重传安装包，不会等待构建结束。改名或后续步骤失败时可重试；远程部分步骤可能已经生效，`not_confirmed` 表示尚未完整核验。安装包文件名与 `windows-v*` 标签保持原样。线上改名完成前，本文中的新地址暂不可作为已上线的证明；本地文件与脚本准备完成也不等于 GitHub 已更新。

[0.7.0 正式版已发布](https://github.com/bsxucome/CodexMeter-for-Windows/releases/tag/windows-v0.7.0)。`windows-v0.7.0` 对应提交 `0768290fbcd91c5ecfd6cd618ea948c09199bd57`；[标签构建及发版任务](https://github.com/bsxucome/CodexMeter-for-Windows/actions/runs/39246290825)已成功。两个运行时的 ZIP 和两份 SHA-256 文件均已上传，中文发行说明已包含在 Release 页面中。

便携包已有独立 Windows 发行地址；签名更新地址已预设为本仓库，等待正式更新清单与可信签名公钥上线。程序不使用 raycalrui/CodexMeter 的 macOS 发行包。清单签名使用 RSA PKCS#1 SHA-256，和 Windows Authenticode 是两套独立机制：前者验证更新发布者，后者用于 Windows 对可执行文件的签名识别。

## 构建

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File windows/scripts/build.ps1 -Runtime Framework -Publish
powershell -NoProfile -ExecutionPolicy Bypass -File windows/scripts/build.ps1 -Runtime DotNet -Publish
```

DotNet 需要 .NET 10 SDK，生成 win-x64 自包含文件夹与 ZIP；Framework 生成 Windows 4.8+ 兼容包。可用 `windows/scripts/setup-dotnet.ps1` 从微软官方元数据下载 SDK 并核对 SHA-512，只安装到项目 `.tools/dotnet`。ZIP 根目录直接包含应用文件，测试失败阻止打包，SHA-256 伴随文件写在 ZIP 旁。

`.github/workflows/windows.yml` 将版本预检、两种运行时的构建测试与打包、版本标签创建和 Release 发布放在同一条流水线。普通 main／PR 提交只构建打包；手动选择 `release` 或推送 `windows-v*` 标签时发版。只有发行任务获得 contents:write，其余任务只读。此前 `0768290` 的标签构建与 0.7.0 正式发版已完成；新增的手动一体化入口需在同步工作流后使用。

## 发布到 GitHub

公开仓库：`bsxucome/CodexMeter-for-Windows`。2026-10-19 用户在正常 PowerShell 完成 GitHub CLI 授权并创建仓库，随后用直接 Git 命令成功推送 main。首次脚本推送失败的具体原因尚未确认；当前 Codex 执行环境仍无法连接 GitHub，不能独立检查托管构建。脚本会复用匹配的 origin 与已有仓库继续推送，不需要重复建仓。

在正常网络的 Windows PowerShell 中，从项目根目录运行：

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File windows/scripts/publish-github.ps1 -Owner bsxucome -RepositoryName CodexMeter-for-Windows -Visibility public
```

脚本扫描暂存源码并生成 `windows/artifacts/CodexMeter-Windows-source.zip`，拒绝认证／私钥／用户数据库与产物入库。没有 GitHub CLI 时，从官方 Release 下载并校验 SHA-256；随后走 GitHub 浏览器授权，按已登录的 GitHub 身份填写本地提交信息，创建公开仓库并推送 main。不会读取 Chrome 凭据、覆盖不相关 origin 或强推；遇到已有同名仓库且无匹配 origin 时停止。可用 `-PrepareOnly` 离线生成源码包。

仓库可能由 Codex 沙箱账号创建，普通用户运行 Git 时会遇到 `detected dubious ownership`。发布脚本通过 Git 的进程配置，仅把自身项目的绝对路径加入本次执行的 `safe.directory`，并传给 GitHub CLI 的 Git 子进程；正常结束或报错后恢复原环境。不修改全局信任列表，也不需要管理员权限。该处理不赋予额外文件写入权限。`publish-ownership-smoke.ps1` 在隔离夹具中验证同类拒绝、源码打包、既有配置保留与异常退出恢复。

Windows PowerShell 5 会把重定向的原生命令 stderr 转成错误记录。发布脚本使用独立进程流并检查退出码，允许“尚未登录／仓库不存在”进入对应分支，也保留授权交互和 Git 推送进度；真正失败仍会停止。`publish-flow-smoke.ps1` 使用模拟 GitHub CLI 和本地裸仓库，验证完整提交／推送、登录失败及已有仓库保护，不访问真实 GitHub。

捕获的 Git stderr 会作为普通诊断显示，推送失败同时报告 Git 退出码和具体原因，常见 Token 及 URL 中的认证信息会遮蔽。隔离远程仓库的拒绝钩子验证了失败原因可见；不使用强推或删除远程仓库重试。

已完成授权和 origin 配置后，也可在正常 PowerShell 中直接同步本地提交：

```powershell
git -c safe.directory="D:/Codex Project/CodexMeter" -C "D:\Codex Project\CodexMeter" push --set-upstream origin main
```

维护此仓库时，也可从项目根目录双击 `Sync-CI-Fix.cmd`。它校验 main 和精确的 GitHub 推送地址，只推送现有提交，再等待该 SHA 的 `windows.yml` 工作流完成。结果写入 `windows/artifacts/ci-logs/latest-result.txt`；失败时附带失败步骤日志。不会暂存、提交、强推或重新创建仓库。等待最多 35 分钟，仍在排队／运行时会如实记录状态。

同步时若出现 `schannel: failed to receive handshake, SSL/TLS connection failed`，脚本会使用命令级的 `http.sslBackend=openssl` 和 `http.version=HTTP/1.1` 重试一次，仍启用证书验证，不修改用户或仓库 Git 配置。两次均推送检查时的同一个提交，避免重试期间本地分支变化造成推送对象改变。认证、证书、分支拒绝和普通连接失败不触发该兼容重试；若仍失败，控制台保留原始 Git 报错，脚本停止，不进入 CI 等待。此项兼容处理不能保证修复代理、防火墙或网络不可达问题。

单独采集最新一次工作流可双击 `Collect-CI-Logs.cmd`；它不推送、不写 GitHub。最新成功不会被更早的失败覆盖。两种入口复用正常用户会话已登录的 GitHub CLI，当前隔离 Codex 桌面不读取 Edge 登录信息。日志留在被 Git 忽略的 artifacts 内。

新版本推荐直接在 GitHub Actions 发版：

1. 更新并提交版本号，使 `ProductVersion.cs` 的 `Current`／`Assembly`、Windows 项目的 `Version` 和 `app.manifest` 版本一致。例如下一版本使用 `0.7.1`／`0.7.1.0`，并可添加 `windows/releases/0.7.1.md`。
2. 打开仓库 **Actions → Windows 构建与发布 → Run workflow**，分支选择 `main`，`operation` 选择 `release`。
3. 同一次运行依次完成版本预检、Framework／.NET 10 构建测试、打包、SHA-256 校验、创建 `windows-v<版本>` 标签以及发布 Release。结束后在发布步骤的 Summary 点击发行链接。

`operation=build` 仅构建、测试和保存 artifacts；它可以用于 PR 或手动检查，当前版本已发布也可执行。`release` 使用仓库中的版本号，不自动增加版本，也不修改源码。0.7.0 已发布，再次选择相同版本会在构建前提示更新版本号。手动发版只允许从 main 运行；已有标签指向另一提交、已有 Release、版本元数据不一致或任一构建失败都会阻止发布。

三个任务都检出本次事件固定的提交 SHA。发行任务先核对两个 ZIP 的校验文件与检出提交，再创建标签；同一版本的发行任务串行执行，不覆盖标签或已发布资产。使用 Actions 自带的 `GITHUB_TOKEN`，不需要额外个人令牌。由它创建标签通常不会再次触发 push 工作流，因此发版继续在当前运行中完成。可选更新签名密钥的配置方式保持不变。

也可继续使用项目根目录的 `Publish-Release.cmd` 本地入口，或运行：

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File windows/scripts/release-github.ps1
```

入口复用当前用户的 Git／GitHub CLI 登录，只操作 `bsxucome/CodexMeter-for-Windows`。它要求 main 且已跟踪文件无未提交改动，不自动暂存或提交。先同步现有提交并等待该 SHA 的 main 构建通过，再按 `ProductVersion.Current` 创建并推送标签；随后等待同一 SHA、同一标签的构建与发行任务完成。既有本地或远程标签指向其他提交时停止，不覆盖标签。已有匹配版本的公开 Release 且四项资产齐全时直接返回现有发行地址。

发布结果保存在 `windows/artifacts/release-logs/latest-result.txt` 和 `.json`。只有实际查询到公开 Release、正确版本及两个已上传 ZIP 和各自的 SHA-256 附件后，才记录 `published`；构建失败、未完成、网络错误或附件缺失均不记录成功。日志采集器的 `-Reference` 可选 `main` 或 Windows 版本标签，并写出机器可读的 `ci-logs/latest-result.json`，避免把同一提交的 main 成功误认为标签发版成功。中途断网可重跑相同入口，保留已有提交与标签。

如果自行维护发行过程，也可在确认 main 构建通过后手动创建标签：

```powershell
git tag windows-v0.7.0
git push origin windows-v0.7.0
```

标签必须与 `ProductVersion.Current` 一致。CI 重新构建／检查两种包；`publish-release.ps1` 逐一复核 ZIP 的 SHA-256，再发布版本资产，不覆盖已有版本 Release。版本说明从 `windows/releases/<版本>.md` 读取，再补充实际签名资产状态；`0.7.0.md` 已包含下载选择、功能和已知使用范围。没有更新私钥 secret 时只发布便携包，不提供签名更新源。

如需启用签名更新，在仓库 Actions secret `WINDOWS_UPDATE_PRIVATE_KEY_XML` 配置自己的 RSA 私钥。发行构建签名对应通道、每个运行时的清单及公钥，并同步到独立 `windows-update-feed` Release（不标记为 latest）。程序默认地址为 `https://github.com/bsxucome/CodexMeter-for-Windows/releases/download/windows-update-feed/{channel}-{runtime}.json`，与该同步机制一致；目前未配置正式密钥，预设地址不代表清单已上线。公钥仍需通过可信渠道手动配置；程序默认不会信任网上新出现的公钥。

首次安装和旧版空地址使用该默认值，已有自定义地址、公钥及通道保持原值。“使用 GitHub 更新源”只修改待保存地址，保留通道与公钥；可先保存地址，但没有公钥时不能开启自动检查／自动安装。稳定版与测试版分别使用 stable-*／beta-* 资产，不依赖 latest 选择。不存在的资产不会回退到另一通道或绕过签名校验。手动下载可点击“下载 Windows 版本”。

有代码签名证书后，设置进程环境变量 `CODEXMETER_PFX_PASSWORD`，再向构建脚本传入 `-SigningCertificatePath <本地PFX> -TimestampServer <证书供应商时间戳地址>`。签名发生在打包之前，仅签本项目 EXE 和 Core DLL；缺少证书时不伪造签名。证书和密码应存入 CI secrets，PFX 只落到临时目录并及时删除，不进入 artifacts。本环境无证书，签名步骤尚未实测。

## 生成签名发行清单

准备自己的 RSA 2048–8192 位私钥 XML（`RSACryptoServiceProvider.ToXmlString(true)` 格式），保存在仓库和打包目录之外。私钥不随应用分发，不能将测试私钥用作正式信任根。脚本也支持通过 `CODEXMETER_RELEASE_KEY_XML` 进程环境变量提供私钥。

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File windows/scripts/sign-release.ps1 `
  -Archive windows/artifacts/CodexMeter-Windows-0.7.0-framework-x64.zip `
  -Version 0.7.0 -Channel stable -Runtime framework-x64 `
  -PackageUrl https://YOUR-HOST/releases/CodexMeter-Windows-0.7.0-framework-x64.zip `
  -PrivateKeyFile D:\PRIVATE\release-key.xml -OutputDirectory windows/artifacts/feed
```

上述地址和私钥路径是待替换示例，不是已提供的发行源。上传 ZIP 以及生成的 `stable-framework-x64.json` 到 HTTPS 服务，再通过可信渠道分发 `public-key.json`。在“关于与更新”填写清单地址与公钥 JSON，手动保存并检查。清单地址可用 `{channel}`、`{runtime}` 占位符，如 `https://YOUR-HOST/feed/{channel}-{runtime}.json`。Beta 清单使用 `beta-*`；两个运行时分别发布清单，不能混用。

外层清单仅为 `payload`、`signature` 两个 base64 字段。签名针对 payload 解码后的确切 UTF-8 字节，不对 JSON 再序列化。Payload 包含 `product=CodexMeter.Windows`、SemVer `version`、`channel`、`runtime`、HTTPS `url`、包 `size/sha256`、`notes` 与每个解压文件的 `path/size/sha256`。公开密钥格式为 `{ "modulus": "base64", "exponent": "base64" }`，不接受含私钥字段的配置。当前上限是 512 MB ZIP、1 GB 解压总量、4096 文件、512 KB 外层清单。

## 安装和回退

程序下载到 `%LOCALAPPDATA%\CodexMeter\updates\staging`，辅助进程重新核验同一配置的公钥、通道、运行时、签名和文件；准备完成后旧版才退出。新版解压到旧版本同级的新目录，等待旧进程退出时核对 PID、映像路径与进程启动时间。新版进入 UI 消息循环后写入固定目录的就绪凭据；启动失败返回旧版，旧目录不覆写、不删除。

只有启动项仍指向旧版本的精确带引号路径时，成功启动后才迁移当前用户 Run 值；不会改 Windows 启动批准状态。启动项迁移失败不删除新旧版本。最近结果保存在 `updates\last-result.json`，只含概括状态、版本和时间。不包含更新私钥、远程凭据或原始服务响应。

确认新版正常使用后，可退出程序，手动删除不再需要的旧版本目录和 `updates\staging` 下载缓存。启动失败的新目录也会保留以供检查；当前不递归删除程序目录。历史、设置、恢复前备份和加密渠道配置独立保存在 LocalApplicationData，不随便携程序目录移动。

v0.7.0 通过隔离的真实辅助进程、WPF 新版启动、旧进程回退及篡改包拒绝测试；进程路径被限制在专属 GUID 测试目录，没有修改正式安装。两种运行时的 main 托管构建及正式标签发版已完成；签名更新源、Authenticode 和正常账号环境更新仍分别验收，不能由发版成功推断。正常桌面的其余检查见 ACCEPTANCE.md。
