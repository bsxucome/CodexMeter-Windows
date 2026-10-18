# Contributing

This repository is an independent Windows adaptation of [raycalrui/CodexMeter](https://github.com/raycalrui/CodexMeter). Preserve its MIT attribution. The Swift/macOS checkout is not included; a cached webpage is not a verified upstream baseline. Use `windows/scripts/sync-upstream.ps1` to obtain a pinned reference before claiming a complete source comparison.

Build with `windows/scripts/build.ps1 -Runtime Framework` or install a local .NET 10 SDK using `windows/scripts/setup-dotnet.ps1` and select `-Runtime DotNet`. Keep shared sources C# 5 compatible. Update product versions in `ProductVersion.cs`, the Windows project and manifest together.

Run PowerShell syntax/encoding validation, core tests, publishing fixtures and process update tests. Run UI scripts sequentially: previews share a mutex. Never close an existing user instance for a test. Test app-server calls with mocks unless the user explicitly authorizes a real-account probe. Do not read authentication files, copy credentials or publish raw provider errors.

Report actual results in `windows/VERIFICATION.md`. A successful preview does not establish real Explorer, physical pointer, multi-monitor, notification delivery or DPAPI acceptance. Use `windows/PARITY.md` for implemented features and remaining platform limits. Keep history/account boundaries, backup rollback and signed-update checks independently testable.

The main panel and settings use compact typography and divider-separated rows. The main panel hides its scrollbar while retaining scrolling; independent management windows use narrow scrollbars and fixed actions. Developer fixtures must remain separate from production accounts, data, channels and updates.
