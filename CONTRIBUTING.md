# 参与贡献

欢迎为 **CodexMeter for Windows** 提交问题、功能建议、文档或代码。可以使用中文或英文交流；项目默认提供中文介绍，[English overview](README.en.md) 提供英文功能与使用说明。

## 开始之前

请先检索已有问题，再说明使用场景、复现步骤、预期与实际表现。提交日志或截图前，请移除账号信息和渠道凭据。不要上传认证文件、访问令牌或私人历史。

本项目参考 [raycalrui/CodexMeter](https://github.com/raycalrui/CodexMeter) 独立实现，需保留 MIT 许可与上游版权声明。当前工作目录不包含完整 Swift/macOS 源码；完整差异复核需通过 `windows/scripts/sync-upstream.ps1` 获取固定提交，不能把缓存网页视为已核验基线。

## 构建与检查

- 使用 `windows/scripts/build.ps1 -Runtime Framework` 构建，或安装 .NET 10 SDK 后选择 `-Runtime DotNet`。共享代码保持 C# 5 兼容。
- 修改版本时，同时更新 `ProductVersion.cs`、Windows 项目与应用清单。
- 运行 PowerShell 语法／编码检查与相关测试。发布工具、更新进程与界面修改需执行对应隔离检查；界面预览共享互斥锁，必须串行运行，不能关闭已有用户实例。
- App Server 默认使用模拟测试。真实账号探测需要明确授权；不得读取认证文件、复制凭据或公开原始服务错误。

## 界面与数据约定

主面板与设置采用紧凑字号和分隔线布局。主面板隐藏滚动条外观并保留滚动；独立管理窗口使用细滚动条与固定操作栏。演示、测试与正式账号、历史、渠道及更新安装必须隔离。

历史账号边界、备份回滚与签名更新校验应保持可独立测试。请将实际验证结果写入 `windows/VERIFICATION.md`，在 [功能对照](windows/PARITY.md) 中保留未完成事项。预览成功不等于真实 Explorer、多显示器、物理点击、通知投递或 DPAPI 已完成验收。
