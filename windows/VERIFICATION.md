# Verification — Windows v0.7.0

Verified on 2026-10-18, Windows 11 x64.

Publication tooling follow-up, 2026-10-19: publish-ownership-smoke.ps1 uses Git's foreign-owner test mode with an isolated global config and a fixture path containing spaces. It reproduces dubious ownership, then verifies successful source preparation with zero and one pre-existing runtime config entries, exact environment restoration, unchanged global config and restored ownership rejection after the script exits. An intentional user-data staging failure also restores configuration. This does not claim GitHub login or remote push success. Application code and portable binaries are unchanged.

A subsequent PowerShell 5 fix launches Git/GitHub CLI with explicit native streams instead of treating expected stderr as a terminating NativeCommandError. publish-flow-smoke.ps1 runs under ErrorActionPreference=Stop with a compiled fixture CLI: logged-out stderr proceeds to interactive authorization, missing-repository stderr proceeds to public creation, and ordinary progress permits a real commit and push into an isolated local bare repository. It also checks nonzero login failure and existing-repository refusal before creation/push. Space-containing arguments are exercised; no credentials, browser session or remote network are used. The ownership regression suite and PowerShell syntax checks also pass after this change.

## Final release checks

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
- User authorized public target bsxucome/CodexMeter-Windows, but no repository has been created/pushed: terminal networking and available GitHub authentication are blocked. The publication attempt fails while fetching GitHub CLI metadata. A source archive and browser-authorization publication script are ready for a normal network session. Chrome login is not CLI authorization.
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
