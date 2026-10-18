# Windows 发行与更新接入

更新源保持未配置，直到有独立的 Windows 发行地址与可信签名公钥。程序不使用 raycalrui/CodexMeter 的 macOS 发行包。清单签名使用 RSA PKCS#1 SHA-256，和 Windows Authenticode 是两套独立机制：前者验证更新发布者，后者用于 Windows 对可执行文件的签名识别。

## 构建

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File windows/scripts/build.ps1 -Runtime Framework -Publish
powershell -NoProfile -ExecutionPolicy Bypass -File windows/scripts/build.ps1 -Runtime DotNet -Publish
```

DotNet 需要 .NET 10 SDK，生成 win-x64 自包含文件夹与 ZIP；Framework 生成 Windows 4.8+ 兼容包。可用 `windows/scripts/setup-dotnet.ps1` 从微软官方元数据下载 SDK 并核对 SHA-512，只安装到项目 `.tools/dotnet`。ZIP 根目录直接包含应用文件，测试失败阻止打包，SHA-256 伴随文件写在 ZIP 旁。

`.github/workflows/windows.yml` 在 main／PR／手动触发时构建两种运行时并保存 artifacts；`windows-v*` 标签在两种构建均通过后创建公开 Release。只有发行任务获得 contents:write，其余任务只读。当前尚未推送仓库，托管工作流未运行，本地只生成 Framework 包。

## 发布到 GitHub

公开仓库：`bsxucome/CodexMeter-Windows`。2026-10-19 用户在正常 PowerShell 完成 GitHub CLI 授权并创建仓库，随后用直接 Git 命令成功推送 main。首次脚本推送失败的具体原因尚未确认；当前 Codex 执行环境仍无法连接 GitHub，不能独立检查托管构建。脚本会复用匹配的 origin 与已有仓库继续推送，不需要重复建仓。

在正常网络的 Windows PowerShell 中，从项目根目录运行：

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File windows/scripts/publish-github.ps1 -Owner bsxucome -RepositoryName CodexMeter-Windows -Visibility public
```

脚本扫描暂存源码并生成 `windows/artifacts/CodexMeter-Windows-source.zip`，拒绝认证／私钥／用户数据库与产物入库。没有 GitHub CLI 时，从官方 Release 下载并校验 SHA-256；随后走 GitHub 浏览器授权，按已登录的 GitHub 身份填写本地提交信息，创建公开仓库并推送 main。不会读取 Chrome 凭据、覆盖不相关 origin 或强推；遇到已有同名仓库且无匹配 origin 时停止。可用 `-PrepareOnly` 离线生成源码包。

仓库可能由 Codex 沙箱账号创建，普通用户运行 Git 时会遇到 `detected dubious ownership`。发布脚本通过 Git 的进程配置，仅把自身项目的绝对路径加入本次执行的 `safe.directory`，并传给 GitHub CLI 的 Git 子进程；正常结束或报错后恢复原环境。不修改全局信任列表，也不需要管理员权限。该处理不赋予额外文件写入权限。`publish-ownership-smoke.ps1` 在隔离夹具中验证同类拒绝、源码打包、既有配置保留与异常退出恢复。

Windows PowerShell 5 会把重定向的原生命令 stderr 转成错误记录。发布脚本使用独立进程流并检查退出码，允许“尚未登录／仓库不存在”进入对应分支，也保留授权交互和 Git 推送进度；真正失败仍会停止。`publish-flow-smoke.ps1` 使用模拟 GitHub CLI 和本地裸仓库，验证完整提交／推送、登录失败及已有仓库保护，不访问真实 GitHub。

捕获的 Git stderr 会作为普通诊断显示，推送失败同时报告 Git 退出码和具体原因，常见 Token 及 URL 中的认证信息会遮蔽。隔离远程仓库的拒绝钩子验证了失败原因可见；不使用强推或删除远程仓库重试。

已完成授权和 origin 配置后，也可在正常 PowerShell 中直接同步本地提交：

```powershell
git -c safe.directory="D:/Codex Project/CodexMeter" -C "D:\Codex Project\CodexMeter" push --set-upstream origin main
```

首次 main 构建通过并检查 artifacts 后，再创建发行标签：

```powershell
git tag windows-v0.7.0
git push origin windows-v0.7.0
```

标签必须与 `ProductVersion.Current` 一致。CI 重新构建／检查两种包；`publish-release.ps1` 逐一复核 ZIP 的 SHA-256，再发布版本资产，不覆盖已有版本 Release。没有更新私钥 secret 时只发布便携包，不提供签名更新源。

如需更新源，在仓库 Actions secret `WINDOWS_UPDATE_PRIVATE_KEY_XML` 配置自己的 RSA 私钥。标签构建签名每个运行时的 Stable／Beta 清单及公钥，并同步到独立 `windows-update-feed` Release（不标记为 latest）。拟用地址为 `https://github.com/bsxucome/CodexMeter-Windows/releases/download/windows-update-feed/{channel}-{runtime}.json`；这只是上线后的目标，当前不可作为可用发行源。公钥仍需通过可信渠道手动配置；程序默认不会信任网上新出现的公钥。

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

v0.7.0 通过隔离的真实辅助进程、WPF 新版启动、旧进程回退及篡改包拒绝测试；进程路径被限制在专属 GUID 测试目录，没有修改正式安装。真实托管源、Authenticode 和正常账号环境更新仍未验收；.NET 10 本地无 SDK，CI 定义不代表已成功运行。正常桌面的其余检查见 ACCEPTANCE.md。
