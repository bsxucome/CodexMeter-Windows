# Verification — Windows v0.7.3

## 0.7.3 标签发布修正（2026-10-20）

正常用户日志确认 c3f6beb 的 [main 运行 39445082151](https://github.com/bsxucome/CodexMeter-for-Windows/actions/runs/39445082151) 成功；随后 [windows-v0.7.2 运行 39445756687](https://github.com/bsxucome/CodexMeter-for-Windows/actions/runs/39445756687) 失败，未发布 Release。DotNet 已完成编译，但仅发版时执行的签名步骤将含中文的内联 run 写成无 BOM 文件，Windows PowerShell 按 ANSI 解码后产生字符串终止符错误。Framework 在高级界面测试查找 QuotaRange 时超过原四秒等待期限；相同提交在 main 与本地通过，现象符合自动化就绪时序问题。

签名步骤移至带 UTF-8 BOM 的 sign-actions-release.ps1，工作流内联调用保持 ASCII。validate-source.ps1 新增工作流 PowerShell 片段解析／语法与 ASCII 检查。actions-signing-smoke.ps1 从实际工作流提取调用，以与 Actions 相同的错误策略和无 BOM 包装脚本在 Windows PowerShell 执行；两种运行时的无密钥分支、稳定版／测试版的临时 RSA 签名及验签、无效标签／运行时／仓库拒绝全部通过，并复现旧 ANSI 解码错误。临时测试密钥不用于发行，不联网、不生成生产信任配置。

高级检查先等待进程输入空闲，统一使用十五秒就绪期限并按 HWND 重新获取顶层 UIA 对象，保留全部功能断言。真实 WPF 夹具在收到本地握手后延迟六秒创建 QuotaRange，新等待通过；永不创建的控件仍在期限内失败。advanced-smoke.ps1 的历史、Token、外观预设／应用与可见性流程通过。Framework 0.7.3 构建通过 337 项测试，既有 Actions 发布保护夹具通过；0.7.1／0.7.2 标签保留，新修复使用 0.7.3。新的托管构建与 Release 仍待实际结果确认。

## 0.7.2 云端路径修正（2026-10-20）

正常用户的发布日志确认 0.7.1 已推送，远程标记解析为 f02d229728caf49517df904662e86133a70ac3ac；[main 运行 39439922002](https://github.com/bsxucome/CodexMeter-for-Windows/actions/runs/39439922002) 的 Framework 测试在 Program.Locator 创建 npm 目录时报 PathTooLongException，未确认新的公开 Release。GitHub 检出目录变长，旧夹具的最深目录达 248 个字符，触及 Framework 限制；此前本地较短目录的成功不能覆盖该场景。

测试目录改用保留完整 GUID 位数的 22 字符安全编码和“中 ”前缀，继续覆盖中文／空格路径、npm 原生可执行文件、无效路径和 Node 回退。未禁用测试或修改系统长路径策略。增加 --locator-tests 与 locator-path-smoke.ps1：在与云端 Framework／DotNet 输出目录等长的独立夹具中运行实际查找逻辑，清理范围仅限带标记的本次 GUID 根目录；Framework Actions 加入该检查。

Framework 构建通过 337 项测试。旧 0.7.1 二进制在 93／103 字符基目录均复现 PathTooLongException；新二进制在两个目录均通过。0.7.1 标签保留，修复版统一使用 0.7.2／0.7.2.0；云端发布状态仍需本次新提交和标签的实际结果确认。

进一步将完整源码放入与 GitHub 检出根目录同为 52 字符的独立标记目录，串行执行 Framework 工作流的 17 个检查入口：源码校验、构建测试与打包、路径回归、签名清单夹具、归属／发布／同步／页面／Actions 编排、更新进程、主界面、圆环交互、功能、设置滚动、历史／外观以及发行界面检查全部通过；路径夹具在此环境中的基目录长 107 字符，仍通过原生 npm 查找与 Node 回退。未关闭用户实例。此为本地 Framework 等路径验证，不替代远程 Actions 或 .NET 10 构建结果。

## 0.7.1 发行准备（2026-10-20）

本轮发布设置页滚轮与高级外观布局修正，将 ProductVersion.Current／Assembly、SDK Version 与应用清单统一为 0.7.1／0.7.1.0。发行说明见 releases/0.7.1.md；沿用现有 windows-v* 标签触发的双运行时构建／测试／打包／Release 工作流，不将普通 main 推送改为自动发布。

本地 Framework 0.7.1 构建通过 336 项测试；设置列表及高级外观四种主题／语言组合的滚轮、PageUp、固定预览／底栏与应用检查通过。PowerShell 语法／UTF-8 检查和实际 Actions 版本读取通过。Actions 发布夹具验证只读事件、版本一致性、已有发行保护、标签冲突、校验文件、包篡改与同次发布确认。包验证和截图脚本改为从 ProductVersion 读取版本，避免误查旧 0.7.0 产物。

release-scripts-smoke.ps1 的 11 组隔离编排场景及发行说明／四项附件传递、篡改拒绝检查通过；这些是模拟传输夹具，不能作为远程发行证据。capture-release.ps1 串行生成主面板、设置、远程提醒、数据、关于、历史和高级外观的 14 张浅深色演示截图，输出目录正确使用 preview-0.7.1。

新产物使用独立目录 windows/artifacts/release-0.7.1/，未覆盖或停止用户正在运行的 appearance-fix 实例。本地无 .NET 10 SDK，该运行时需由 Actions 构建。当前 GitHub 443 连接仍失败，尚未确认远程标签、Actions 执行或 0.7.1 Release；本地准备不代表已经发布。生产代码签名、签名更新源与真实 Explorer 验收限制继续保留。

## 高级外观布局统一（2026-10-20）

高级外观继承设置页的 CompactWindow，宽度由 570 改为 380 DIP，统一标题、边距、分隔线和按钮。固定顶部演示预览及底部操作，分组显示参数；滚动区设为 Hidden 并移除右侧留白，保留滚轮与键盘翻页。JSON 格式和“应用后在设置中保存”的流程保持兼容。

Framework 构建及 336 项程序测试通过。settings-scroll-smoke.ps1 增加真实 AppearanceWindow 的浅色／深色、中英文组合检查：窄窗口、无滚动条、滑块上滚轮滚动且不改值、PageUp、固定预览／底栏、末项可达、520 DIP 窗口底部按钮可见、预设与应用均通过；同时保留设置列表滚动回归。advanced-smoke.ps1 通过外观参数应用至任务栏及既有历史流程检查。四种主题／语言组合截图已生成，并人工检查中文浅色与英文深色的布局；未模拟物理鼠标或触控板设备。

ui-smoke.ps1 串行通过设置窗口打开／关闭、隔离实例恢复、任务栏显示和退出检查，验证共用窗口布局未破坏原有入口。

build.ps1 支持 -ArtifactDirectory 将新产物放入独立目录。本次使用 windows/artifacts/appearance-fix/，未覆盖或停止用户正在运行的旧实例。此记录是本地 Framework 验证，不代表新的托管构建或 GitHub 发版。

## 设置页滚动修正（2026-10-20）

修正鼠标位于“弹窗区域顺序”列表时滚轮被内部 ScrollViewer 截住的问题，额度窗口列表使用相同处理：内部可滚动时优先内部滚动，无溢出或到边界时将原始滚轮方向、增量与设备信息交给外层页面。仅设置页的外层与列表竖向滚动条设为 Hidden，并移除右侧额外留白；未禁用滚动，其他管理窗口不变。

settings-scroll-smoke.ps1 在独立 STA 进程中通过反射创建 preview=true 的实际 SettingsWindow，使用 WPF 路由输入与真实布局进行检查。旧 0.7.0 在浅色／深色下均复现滚轮被截住；修复版两种主题均通过列表项目／空白区上下滚动、小增量、选择保留、滚动条隐藏、长列表内部滚动、上下边界交接与 PageDown 检查。夹具不启动应用主入口，不读写正式设置、账号、历史或注册表，不停止用户实例。实际物理鼠标／触控板设备尚未在该测试中模拟。Framework 构建及 336 项程序测试通过；features-smoke.ps1 与 ui-smoke.ps1 串行通过，覆盖设置保存、区域排序、语言／样式切换、独立实例恢复与正常退出。

## 项目页面整理与改名（2026-10-20）

已将公开展示名称统一为 CodexMeter for Windows，准备默认中文首页与英文版 README，重点介绍任务栏额度、历史、Token、定制、提醒与数据管理。问题／建议表单、贡献指南、Actions 显示名称和后续发行文案改为中文；文档中已过时的“尚无发行仓库／托管构建未完成”说明同步修正，实际桌面验收限制继续保留。

用户确认将仓库地址改为 bsxucome/CodexMeter-for-Windows。页面同步入口仅允许该精确仓库及旧地址，先核验管理员权限与远程改名，再改本地 origin；推送不强制、不提交、不创建发行，保留现有主题标签。既有 Release 仅更新标题与中文正文，并回读验证，标签／下载资产保持不变。页面同步与构建完成分开记录，避免把源码上传当成构建成功。

Framework 重新构建通过 336 项测试；同步入口 12 组隔离 CLI 场景、既有 11 组发版编排及同次 Actions 发版夹具均通过。GitHub 页面夹具验证中文 JSON 往返、旧地址迁移、已改名重试、权限／工作区／推送／接口失败、主题保留、仅修改发行文案及错误脱敏。正常用户截图确认 9560814 的 plan 和两个构建任务成功、release 按普通推送策略跳过；不将其视为手动发版路径已完成云端验收。线上改名和本次页面发布仍须以正常用户会话的同步结果核验。

以下保留历史验证记录；其中旧的待验收状态以较新的结果为准，仓库链接已按目标新地址统一。

Verified on 2026-10-18, Windows 11 x64.

Synchronization TLS follow-up, 2026-10-19: the normal-user push of 7a4f7bf failed with Schannel's handshake error before confirming a remote update. Sync-CI-Fix.cmd now retries only recognized Schannel handshake failures once using command-local OpenSSL/HTTP/1.1 and explicit certificate verification. It pushes the inspected SHA to main without force; unrelated native failures stop before CI collection. No global/repository configuration is written. sync-ci-smoke.ps1 passes 11 isolated native-CLI cases: normal success, recovered handshake, persistent handshake failure, unsupported fallback backend, authentication failure, ordinary connection failure, certificate failure, ref rejection, wrong origin, wrong branch and invalid SHA. The fixtures exercise PS5 native stderr, bounded diagnostic tails, command-local flags, exact commit handoff and refusal to collect CI results after push failure. They never contact GitHub or read credentials. The new suite is included in Framework CI. Real network recovery and rollout of the pending Actions workflow remain unconfirmed.

Public Release acceptance, 2026-10-19: [Windows 0.7.0](https://github.com/bsxucome/CodexMeter-for-Windows/releases/tag/windows-v0.7.0) is published, with draft=false and published_at=2026-10-19T11:43:45Z in the captured GitHub API response. Its two ZIPs and two SHA-256 companions each have uploaded state and positive size. The release launcher reports published, and [tag run 39246290825](https://github.com/bsxucome/CodexMeter-for-Windows/actions/runs/39246290825) is completed/success. Local HEAD, origin/main and windows-v0.7.0 all resolve to 0768290fbcd91c5ecfd6cd618ea948c09199bd57. The normal-user CLI provided these results; direct web fetch remains unavailable here. Remote package bytes were not independently downloaded or rehashed locally. This supersedes the pending-publication statements in the historical preparation notes below.

Single-run Actions release follow-up: workflow_dispatch now exposes build/release. A read-only plan validates product/assembly/SDK/manifest versions and rejects existing releases or conflicting refs for publication. All jobs check out github.sha. The release job depends on both matrix builds, has the only contents:write permission and serializes by version tag. Both signing and publication also check the trusted GitHub event/ref directly, rather than trusting plan output alone. Manual-main publication verifies both archive hashes and the immutable checkout before creating a ref, publishes in the same run and confirms public metadata plus four uploaded assets before writing a Summary link. Existing tag-triggered publication and the local launcher remain supported.

actions-release-smoke.ps1 passes read-only push/PR/manual-build checks, manual/tag planning, non-main rejection, version/manifest/ref conflicts, existing-release rejection, lightweight and annotated tags, bounded annotation loops, lost create responses, concurrent conflicting refs, failed ref creation, missing assets, correct GitHub output-file formatting, actual publisher integration, wrong-checkout refusal, pre-tag checksum refusal and a verified Summary link. The real HTTP wrapper is exercised with injected WebException responses: only GET 404 maps to absence, while permission/server errors fail with sanitized messages. A local temporary Git commit is used by the publisher integration; HTTP and release creation remain fixture-only. The legacy 11-case release orchestration suite also passes. These local checks do not claim that the new workflow or manual publication has executed on GitHub; synchronization and a later new-version release are still required for hosted acceptance.

The follow-up Framework build passes all 336 scenarios with warnings-as-errors. The real source version reader also reports 0.7.0 and publish=false for a normal main push, without accessing publication APIs. All PowerShell scripts pass syntax/encoding validation. Application C# sources and the existing 0.7.0 tag/assets are unchanged by this workflow update.

Hosted CI acceptance, 2026-10-19: the normal-user collector records [run 39182365683](https://github.com/bsxucome/CodexMeter-for-Windows/actions/runs/39182365683) as **completed / success**, with headSha **025662551bf6010350b5791349eefcd37bb900f2**. Both ignored latest-result.txt and runs.json report that result; local main and origin/main match the same SHA. This supersedes the pending-hosted-run statements in the historical correction notes below.

Release automation preparation: Publish-Release.cmd and release-github.ps1 synchronize committed main, require successful CI for that SHA before creating a version tag, then separately require tag CI success and query actual public Release metadata. Conflicting local/remote tags are refused; already published matching releases are reused. Both portable ZIP/checksum pairs must be present and uploaded before a published result is written. The collector now supports an explicit main/tag reference, checks headBranch as well as SHA and writes structured JSON alongside the text handoff.

release-scripts-smoke.ps1 passes 11 network-free orchestration scenarios: success, failed main CI, failed tag CI, wrong origin, dirty tracked state, missing authentication, conflicting local tag, conflicting remote tag, Release query network error, missing asset and existing matching release. Native Git progress goes through the real process-stream wrapper. A newer successful main run with the same SHA is deliberately returned before the correct tag run and is not mistaken for release success. The actual CI publisher also passes four verified asset paths and prepared notes to a fixture CLI; modified package bytes are rejected before release creation. Fixture CLIs require an explicit root marker, write only isolated GUID-root state, and never contact GitHub. These checks do not claim an actual public Release; the normal-user handoff must still complete.

The workflow at that SHA requires Framework and DotNet build/test/package matrix jobs, sequential Framework publishing/process/UI smoke checks and artifact uploads without continue-on-error. Overall success therefore confirms those required stages completed. This result is collected through the user's authenticated CLI session; direct web access still returns a cache miss here. Successful per-job logs and artifact contents were not downloaded, so no new hosted scenario count or independently checked artifact hash is claimed. This main-branch run does not execute the tag-only GitHub Release publication job.

Publication tooling follow-up, 2026-10-19: publish-ownership-smoke.ps1 uses Git's foreign-owner test mode with an isolated global config and a fixture path containing spaces. It reproduces dubious ownership, then verifies successful source preparation with zero and one pre-existing runtime config entries, exact environment restoration, unchanged global config and restored ownership rejection after the script exits. An intentional user-data staging failure also restores configuration. This does not claim GitHub login or remote push success. Application code and portable binaries are unchanged.

A subsequent PowerShell 5 fix launches Git/GitHub CLI with explicit native streams instead of treating expected stderr as a terminating NativeCommandError. publish-flow-smoke.ps1 runs under ErrorActionPreference=Stop with a compiled fixture CLI: logged-out stderr proceeds to interactive authorization, missing-repository stderr proceeds to public creation, and ordinary progress permits a real commit and push into an isolated local bare repository. It also checks nonzero login failure and existing-repository refusal before creation/push. Space-containing arguments are exercised; no credentials, browser session or remote network are used. The ownership regression suite and PowerShell syntax checks also pass after this change.

User-reported progress, 2026-10-19: GitHub CLI authentication as bsxucome and creation of public bsxucome/CodexMeter-for-Windows succeeded in the normal user session; the first script push failed without a visible Git reason. A direct normal-session Git push then succeeded, creating main and setting its upstream; local origin/main records b68c6d8. The original script failure cause is not established. The publisher now captures and displays stderr from captured Git commands as ordinary diagnostics, redacts common credential forms, and includes Git's exit code and message in push failures. A fourth flow fixture uses an actual rejecting pre-receive hook and verifies the rejection is reported. The isolated Codex environment still cannot connect to GitHub or read Actions results; hosted build/release acceptance is pending. Publication-diagnostic follow-up changes require a separate push after their local commit.

## Final release checks

CI correction follow-up, 2026-10-19: logs collected from [run 39147240924](https://github.com/bsxucome/CodexMeter-for-Windows/actions/runs/39147240924) at b68c6d8 confirm two independent failures. The DotNet job reports 11 CA1416 errors in RegistryStartupValueStore; Core and Tests now target net10.0-windows, and the manually maintained WPF AssemblyInfo has its platform annotation and version metadata in both builds. Warnings-as-errors and the analyzer remain enabled. This environment still has no .NET 10 SDK; the corrected SDK build must be verified by the next hosted run.

Framework's first real transport test fails under a UTF-8 console. The unmodified binary reproduces the same disconnect locally with CP65001 and a three-byte encoding preamble. Framework's Process.Start creates and auto-flushes a StandardInput writer using Console.InputEncoding. RPC startup now uses a BOM-free encoding while preserving the console code page and restoring the exact previous encoding afterward; modern .NET sets StandardInputEncoding directly. A new real-child regression checks restored encoding and a first JSON request with fragmented Chinese/Emoji echo. The mock emits bytes rather than individual surrogate chars. The corrected Framework build passes **334 scenarios** both in the original console configuration and in a UTF-8 console. Existing running v0.7.0 is retained; the old portable ZIP is not replaced by this source-only CI correction.

The read-only collector was validated with fixture CLI output for failure, a newer success and no matching run. Sync-CI-Fix.cmd pushes existing committed main only to the exact authorized origin, then waits for that exact SHA; mocked Git/CLI tests cover asynchronous completion, ignoring a different older failed run, refusal of unrelated origins and stopping after push failure. These fixtures never contact GitHub. Latest results are written under ignored windows/artifacts/ci-logs/latest-result.txt. Selecting the first executable match also avoids treating multiple PATH candidates as one executable name. Public-network verification still requires the normal user session.

Second hosted correction, 2026-10-19: [run 39150220336](https://github.com/bsxucome/CodexMeter-for-Windows/actions/runs/39150220336), at 4cc87cc, confirms the earlier platform/transport fixes got past their original failures: DotNet Core compiles, and Framework build/test/package plus signed-publisher smoke complete. The run still fails downstream: WFO0003 in the WPF SDK project, and staged whitespace validation in the publishing ownership fixture.

WPF retains its PerMonitorV2 manifest, which establishes DPI awareness before window creation in both runtimes. The project now exempts only the WinForms WFO0003 bootstrap diagnostic with an explicit rationale; it does not switch the application to WinForms startup, change DPI behavior, disable the platform analyzer or relax other warnings-as-errors. Microsoft documents both [WPF manifest configuration](https://github.com/microsoft/WPF-Samples/blob/main/PerMonitorDPI/readme.md) and [scoped WFO0003 suppression](https://learn.microsoft.com/dotnet/desktop/winforms/compiler-messages/wfo0003). The .NET 10 SDK remains unavailable locally, so hosted verification is still required.

The old ownership fixture reproduces the hosted whitespace failure locally when its publisher copy uses CRLF. Its dummy .gitattributes omitted text normalization. Ownership and publication-flow fixtures now declare valid text attributes and always construct CRLF script copies, even from an LF checkout. Both corrected suites pass, including exact Git configuration restoration, the user-data staging refusal, actual local bare-repository push, expected authorization/existing-repository refusals and remote rejection diagnostics. The real publisher and its strict whitespace/privacy checks are unchanged. An excessively nested reproduction directory exceeded Git's local object-directory path limit; the complete flow suite passes from the normal project fixture root. No production remote is involved.

The follow-up Framework build again passes all 334 scenarios with warnings-as-errors. This is a source/configuration correction; the user's running versioned application and previous portable ZIP remain unchanged. The next hosted run must still verify both jobs and artifact uploads.

After this correction, all Framework workflow smoke scripts also pass locally: publishing, ownership, publication flow, update-process, UI, indicator, features, advanced and release. UI checks run sequentially and preserve the original user process (PID 4980). Feature automation paused during style switching, then completed all five styles and Traditional Chinese without intervention; no preview was forcibly stopped. Source preparation and staged whitespace/privacy checks pass with 113 tracked source files. These local checks do not substitute for the pending .NET 10 build and hosted artifact upload.

Third hosted correction, 2026-10-19: [run 39151322090](https://github.com/bsxucome/CodexMeter-for-Windows/actions/runs/39151322090), at 9c49f3c, compiles the .NET 10 main application with zero warnings and errors. Both publisher fixture suites and the real updater process checks pass. The run still fails later: DotNet's corrupt-settings test throws JsonReaderException, and Framework's UI smoke cannot find the popover after closing Settings.

The settings guard compared an exception's exact type name with JsonException, missing its derived reader exception. Json.Parse now catches the typed [System.Text.Json.JsonException](https://learn.microsoft.com/dotnet/api/system.text.json.jsonexception) base class in the modern adapter and emits a safe FormatException without input text or an inner parser exception. Framework's parser failures use the same contract. Existing settings, remote-response and restore-journal guards already handle FormatException. New syntax/depth regressions verify that malformed private content is not retained. The complete Framework suite passes **336 scenarios**. A separate probe compiles the actual modern JSON adapter against the installed .NET 6 runtime and confirms syntax/depth normalization plus valid Chinese text. A supplemental modern-branch run also passes 336 scenarios on that runtime, including corrupt settings, malformed remote responses, interrupted restore and real child transport. That supplemental build uses the old Framework compiler, explicit runtime references and one equivalent explicit LINQ lambda in a temporary Settings copy to avoid the old compiler's Span overload ambiguity; production Settings.cs is unchanged. This is additional compatibility evidence, not a .NET 10 SDK/analyzer/build result.

Opening Settings deliberately hides or deactivates the popover; the old UI smoke searched for it without reopening. Smoke now uses --instance-fixture with an exact marker, GUID-specific mutex and show message, preview data and production dismissal behavior. It reopens the panel through the actual indicator before closing it and launching a second fixture process. Invalid markers exit before UI initialization; the second process exits successfully and reopens the original fixture's hidden HWND. The entire UI smoke passes while the user's existing PID 4980 remains running. No production CLI, account, history, preferences, startup registry or normal-instance broadcast is used by this fixture.

After the changes, indicator input/reopening smoke and real update helper startup/rollback/tamper checks also pass sequentially. Complete .NET 10 tests, packaging, hosted UI checks and artifact uploads remain pending the next run; the .NET 10 compilation at 9c49f3c alone does not establish those results. The user's versioned application and old portable ZIP are retained.

| Check | Result |
| --- | --- |
| Framework compile and core tests | PASS, warnings as errors, 332 scenarios; DPAPI success excluded |
| publishing-smoke.ps1 | PASS, Stable/Beta envelopes verify in application core |
| update-process-smoke.ps1 | PASS, actual healthy startup, actual previous-EXE rollback and tamper rejection |
| ui-smoke.ps1 | PASS; normal user-instance checks skipped to preserve running v0.6.0 |
| indicator-smoke.ps1 | PASS, HWND/WPF hits and five popup cycles; Explorer unavailable, physical focus not injected |
| features-smoke.ps1 | PASS, sections/preferences/languages and all five styles |
| advanced-smoke.ps1 | PASS, calendar/Token navigation, estimates and applied appearance |
| release-smoke.ps1 | PASS, compact pages, remote scope and all three update demos |
| capture-release.ps1 | PASS, 14 light/dark demo images; main and Settings images visually inspected |
| validate-source.ps1 | PASS, PowerShell syntax/encoding and whitespace |

All preview/process checks above ran sequentially against the final v0.7.0 binary. The existing user process was retained. Portable packaging is checked separately after documentation is copied. Local logs and screenshots are under ignored windows/artifacts; they are not source-repository history or proof of hosted deployment.

## Completed

- Framework compatibility build compiles with warnings treated as errors.
- 332 automated scenarios pass for the final Framework build. Windows DPAPI roundtrip is explicitly unverified here; a normal user-profile run with successful DPAPI executes one additional assertion. This count excludes actual message delivery and production update installation.
- New coverage includes per-channel five-hour/weekly scope, Bark icon, Gotify priority, legacy encrypted-profile defaults, default-hidden Reserve visibility, immutable long-history reduction, exact raw hover lookup, indexed SQLite queries and unchanged Token write suppression.
- A real child exits immediately after replacing an isolated SQLite database. The next HistoryStore startup restores the original account rows and identity salt from its durable journal; a separately readable pre-restore backup remains. Pre-replacement interruption is also covered. Malformed recovery metadata maps to a safe IOException before database access and leaves original bytes unchanged. Invalid database field formats fail safely.
- Measurement-only fixtures reduce 200,000 observations to 1,600 display points in approximately 0.3 seconds, preserving raw data, endpoints, extrema and true segment gaps. An isolated 10,001-row indexed quota read measured approximately 31 ms (writes approximately 13 seconds). Results depend on hardware and are not fixed performance guarantees.
- Remote fixtures cover four provider request/auth/confirmation shapes, masked destinations, mention suppression, actual observed replenishment, first-read/account/gap/stale suppression, deduplication/cooldown, response/attempt/delay bounds, cancellation, scope rechecks, malformed JSON, permanent errors, redirects and ambiguous network outcomes without replay. HTTP handlers are injected; no remote messages are sent.
- Secret-profile serialization/deep copies use an isolated vault. Native DPAPI fails with error 2 (user profile unavailable); the .NET ProtectedData API independently reports the same profile limitation. Tests verify that this failure produces no plaintext file and that corrupt encrypted input fails closed. They do not claim successful DPAPI encryption/decryption in this sandbox.
- SQLite online backups are taken from open WAL fixtures with multiple accounts and Token rows. Tests verify exact archive contents, account salt preservation, row counts, corrupted/newer formats, modified hashes/counts, extra/traversal entries, selected/all-account clearing, successful restore and injected failure immediately after database replacement. The failed replacement restores both original rows and salt and leaves a valid independent pre-restore archive.
- Service maintenance waits for an in-flight refresh, clears account selection and notification scope before replacement, then re-identifies the account and resumes history recording. These checks use isolated databases and mock RPC.
- Signed updater fixtures cover SemVer/build/prerelease ordering, stable/beta boundaries, wrong key/tampered signatures, product/runtime mismatch, macOS rejection, zip/file hashes, altered/missing/duplicate/unlisted/symlink entries, unsafe Windows paths, size bounds and HTTPS redirect policy. Core launch-injection checks are supplemented by update-process-smoke.ps1: an actual helper waits for the owned parent to exit, installs the real application into a new fixture sibling, waits for WPF readiness, and launches the old fixture EXE after a forced failed start. A tampered archive is refused before handoff. Old EXE bytes remain unchanged. Every process and file is confined to a marked GUID test root; this is not a production installation.
- PowerShell publisher smoke generates only a temporary test key and fixture ZIP, signs Stable and Beta manifests, and verifies the resulting envelopes/files with the application core. Test private keys are cleaned up and are never shipped as a production trust key.
- Release UI Automation verifies compact settings width, functional ScrollPattern with fixed save footer, independent remote/backups/About windows, four PasswordBox credentials, each channel's five-hour/weekly switches, disabled preview sending/data operations/update installation, editable source/key fields, and all three simulated update states. Light/dark captures are produced for these windows. Demo operations never read production settings/history/credentials.
- Previous optional allowance/null/count/detail parsing, reset expiry/lifetime, balance-only service responses, stale retention, read-only access, section order, safe preference migration, sliding reset storage/rendering and current-cycle anchors remain covered.
- Notification tests cover opt-in defaults, prioritized simultaneous alerts, duplicate snapshots, sustained unsafe state, deferred cooldown reentry, recovery, stale/loading/future/expired data, independent low/pace switches, inclusive thresholds, reset jitter/new cycles and old-account queued notices. Tests evaluate the pure rules without sending OS notifications.
- Startup tests inject an in-memory store: exact quoted paths, no writes during status reads, enable/disable, known disabled and unknown approval layouts, old-release detection and restoring the exact prior command. The production Run and StartupApproved values are not modified by these tests.
- Fullscreen geometry covers primary/secondary and spanning monitors, negative coordinates, maximized working areas and known shell-host exclusions. Actual DWM/process-host identification and Explorer UIA stalls require a real shell desktop; geometry tests do not claim that acceptance.
- Expanded feature UI checks verify saved section ordering (Token before quotas), hidden balance, authoritative demo reset count, notification/startup preference retention, threshold changes and disabled preview notification/system-settings actions. All five updated styles and Simplified/Traditional Chinese plus English switch through Settings.
- v0.4.2 native layered-window checks reproduce two alpha-zero hit-test holes in v0.4.1 and confirm that the fixed production-transparent HWND receives hits at ring centers, corners and glyph gaps. WPF also reports an input target there. These checks instantiate only owned indicator/underlay windows, without CLI, account or persistence access.
- Indicator request checks verify duplicate merging and repeated dispatch without hiding the window. Ten core gesture scenarios cover click jitter, two-axis drag thresholds, release-only movement, duplicate release, capture cancellation and recovery.
- UI Automation with preview data and production dismissal enabled verifies five popup open/close cycles, no overlap with the indicator, and reopening across a placement tick. Foreground focus transfer to an owned helper was denied by this desktop; physical deactivation, pointer delivery and actual Explorer interactions remain unverified. These are not counted as automated mouse clicks.
- Ordinary indicator preview now uses production transparency; only screenshot smoke uses a solid background. The previous opaque preview concealed the production alpha-zero hit-test problem.
- v0.4.1 targeted UI checks confirm that no visible system scrollbar remains in the popover, overflowing content still scrolls through ScrollPattern, header/footer remain fixed, and visible quota percentages stay inside the viewport. Wheel/touchpad handling uses the existing WPF ScrollViewer with Hidden rather than Disabled visibility; physical gestures are not injected into the user's desktop.
- UI, indicator, feature, advanced-history and release-preview script results for the final v0.7.0 build are recorded above. Screenshots use demo allowances and quota data, never production account values.
- Persistence coverage uses the actual Windows SQLite library: persisted identity salt, anonymous boundaries, cross-account isolation, change sampling and 15-minute anchors, Token validation/upserts, retention, clear, CSV privacy/escaping, reopen, and prediction-segment gap/reset boundaries.
- New analytics coverage includes leap months, local DST weeks, duration-specific cycle domains, calendar token grouping across year boundaries, large totals, consumption beyond 100% across resets, lower bounds for clipped/gapped histories, estimate prerequisites and suppression, reset jitter, immutable smoothing and monotone Bezier bounds.
- Appearance tests cover accepted defaults, dependent ring/text bounds, nonfinite values, isolated copies, JSON roundtrip/schema/size, persisted preferences and independent CSV token ranges with Unix/local timestamps.
- Service tests verify that optional Token RPC failures/timeouts keep fresh quota, and late responses after an account boundary cannot update the display or write old Token data. Recording can be disabled while reading existing account history.
- Display tests cover preference migration, deep copies, window filtering/order, five style keys, English labels/composed countdowns and Windows traditional-Chinese conversion.
- UI Automation checks cover the original percentage, StartLeft dropdown, indicator visibility and exit; additional checks cover an independent history window, disabled preview export/clear, immediate English/content/style changes, all five taskbar styles and traditional Chinese.
- New UI checks pass for natural week/month navigation, future bounds, consumption lower bounds, independent weekly/monthly Token grouping, an eligible exhaustion estimate, appearance presets/sliders, applied taskbar dimensions and the weekly-chart visibility switch. Owned appearance windows are discovered under their owning Settings window.
- During interaction testing, timed refresh could replace an open Token selector and lose its choice. Selector state is now retained independently, with rendering deferred while a dropdown is open; account boundaries still clear immediately.
- Popover, indicator, English history and advanced-appearance previews are inspected. The indicator remains content-sized and keeps the 4-DIP Start anchor calculation. History and Token data in screenshots are explicitly demo data. Long-range Token grouping and configurable appearance previews do not read production history or preferences.
- Normal-instance smoke checks are skipped while the user's v0.6.0 is running. User instances are not closed or replaced. A read-only real CLI probe reports home-unavailable before initialization; it does not print account data or raw errors.
- Release packaging excludes tests, screenshots, test-data and keys. Portable extraction/preview, 0.7.0.0 metadata and SHA-256 are checked before delivery; RELEASES.md and ACCEPTANCE.md are included.
- Source preparation scans the staged tree for forbidden data paths and credential-like content and creates a clean Git archive. All PowerShell scripts pass syntax parsing and Chinese scripts require UTF-8 BOM. This does not assert that GitHub Actions YAML or hosted builds have executed successfully.

## Desktop-dependent limits

- Real Explorer taskbar docking is not verified in this isolated test desktop. Start is located by its stable UI Automation ID or native Start control; docking calculations are tested against supplied bounds.
- Indicator remains an independent non-activating overlay, not an Explorer plugin. For Start docking, an occupied or out-of-bounds slot falls back immediately above the button, rather than jumping to a distant gap.
- Windows 11 system backdrop uses documented DWM transient material and rounded-corner attributes. Its actual desktop appearance is not captured by WPF RenderTargetBitmap previews; unsupported systems use the opaque palette.
- Primary horizontal taskbar only. Explorer restart, auto-hide, physical fullscreen transitions and mixed-monitor scaling still need desktop acceptance.
- Real account quota remains unverified here: App Server exits before initialization with the fixed diagnostic home-unavailable. Authentication files and raw errors were not exposed.
- Real account/usage/read Token data is also unverified. Optional endpoint parsing/failure handling is tested with controlled responses; CLI/account availability varies.
- Real credit balances/reset opportunities are unverified; null, count-only, partial detail and lifetime semantics use controlled service responses. Credit IDs remain in memory only and no redemption request exists.
- System notification delivery, clicking a real notification, Do not disturb behavior, actual Run registration and login startup are not exercised in the isolated desktop. NotifyIcon delegates display/suppression to Windows. StartupApproved is undocumented compatibility state: known flags are observed, unknown layouts are reported and never overwritten. Future Windows versions need rechecking.
- Pointer-driven chart tooltip clamping, hover animation and changed-digit color transitions are implemented but not physically exercised by UI Automation. Default light/dark images verify static appearance only.
- Core SQLite persistence is tested, but production history is not opened by preview. CSV and deletion are exercised against isolated test databases, not the user's data.
- Full feature parity is not claimed. Actual provider delivery, normal-user DPAPI, hosted updates, exact upstream pixels/animations and desktop acceptance remain tracked in PARITY.md and ACCEPTANCE.md. The helper/process handoff and initial rollback now pass isolated real-process checks; simulated update state is implemented. Sibling installation requires write access; old directories/caches remain for explicit cleanup. Readiness covers initial startup (30 seconds plus two-second survival), not later crashes.
- User completed browser authorization, public repository creation and the first main push from a normal PowerShell session on 2026-10-19. Terminal networking in the isolated Codex context remains blocked, so hosted CI and releases cannot be independently confirmed here. Chrome login alone is not CLI authorization.
- Feed/public key remain empty and automatic options are off. Test keys cannot authorize production updates. No real certificate or timestamp signing was exercised; this package has no Authenticode signature.
- GitHub Actions defines Framework/.NET 10 build/test/package and a tag-triggered Release job. Build jobs are read-only; only the release job gets contents:write. Optional signed update feeds require the owner's private-key secret. No hosted workflow has run. The local .NET 10 SDK remains unavailable; only Framework 4.8+ was compiled/tested. SDK/bootstrap and upstream-fetch scripts are supplied, but downloads cannot be exercised with current shell networking.
- Upstream checkout/commit SHA remain unavailable; see UPSTREAM.md.

## Repeat

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File windows/scripts/build.ps1 -Runtime Framework -Publish
powershell -NoProfile -ExecutionPolicy Bypass -File windows/scripts/indicator-smoke.ps1
powershell -NoProfile -ExecutionPolicy Bypass -File windows/scripts/ui-smoke.ps1
powershell -NoProfile -ExecutionPolicy Bypass -File windows/scripts/features-smoke.ps1
powershell -NoProfile -ExecutionPolicy Bypass -File windows/scripts/advanced-smoke.ps1
powershell -NoProfile -ExecutionPolicy Bypass -File windows/scripts/release-smoke.ps1
powershell -NoProfile -ExecutionPolicy Bypass -File windows/scripts/publishing-smoke.ps1
powershell -NoProfile -ExecutionPolicy Bypass -File windows/scripts/update-process-smoke.ps1
powershell -NoProfile -ExecutionPolicy Bypass -File windows/scripts/validate-source.ps1
powershell -NoProfile -ExecutionPolicy Bypass -File windows/scripts/capture-release.ps1
```

Run UI/screenshot scripts sequentially because previews share one isolated mutex. Exit the old release before launching a new one: all live releases intentionally share the same production single-instance mutex. Preview mode never writes settings, opens production history or launches Codex.

For baseline reproduction, pass indicator-smoke.ps1 an optional -BaselinePath pointing to the v0.4.1 executable. It loads only the indicator class in a separate owned test process; it does not start the old production application or replace a running release.
