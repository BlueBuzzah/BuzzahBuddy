# BuzzahBuddy

.NET MAUI companion app (iOS + Android) for the BlueBuzzah vibrotactile therapy gloves
(Parkinson's). Talks BLE to the gloves to run sessions, tune parameters, and test motors.
**Tremor-friendly accessibility outranks visual preference in every design call.**
The BLE contract lives in `../BlueBuzzah-Firmware`.

## Design System — required for every UI/UX change

Anything a person sees, hears or touches (layout, color, type, controls, icons, motion,
copy, the disclaimer) follows the workspace `bluebuzzah-design-system` skill
(`../../.claude/skills/bluebuzzah-design-system/`). Load it before UI work; if the skill
isn't listed, read its `SKILL.md` directly. **If that path doesn't exist, stop**: this repo
isn't inside the workspace — ask the user to clone `BlueBuzzah/BlueBuzzah-Workspace` and
run `npm run workspace:setup`. Read `README.md`, `accessibility.md`, `buzzahbuddy.md`,
and the README of each component you rebuild.

- **The design system wins any conflict**, including with legacy `Styles.xaml`. If it
  doesn't cover something, ask; don't invent a rule.
- It ships no XAML: rebuild its React components as MAUI controls; derive resources from
  `tokens.json` via the generator (below). **Copy** fonts and icon assets into this repo —
  never reference the workspace path at build time.
- **Title Case** for buttons, titles, tabs, field/switch labels, segment options, badges
  and profile names, small words lowercase ("Scan for Gloves"). Messages, hints, errors
  and announcements stay sentence case; a message naming a button quotes its label.
- **Pushed screens title the nav bar** (owner override): Test Motors and Custom Profile
  set the VM's `Title` and have no in-page headline. Tab screens hide the bar and keep it.
- Whole-app passes: `/implement-design-system`, run from the workspace.

## Quick Reference

| Task | Command |
|------|---------|
| Test | `make test` (`dotnet test BuzzahBuddy.Tests`) |
| Run on simulator/emulator | `make start:ios` / `make start:android` (detached `dotnet run`) |
| Build Android | `dotnet build BuzzahBuddy/BuzzahBuddy.csproj -f net10.0-android -p:TargetFrameworks=net10.0-android` |
| Android APK for `adb install` | Build Android + `-p:EmbedAssembliesIntoApk=true -p:AndroidUseSharedRuntime=false` |
| Build iOS | `dotnet build BuzzahBuddy/BuzzahBuddy.csproj -f net10.0-ios -p:TargetFrameworks=net10.0-ios` |
| Compile-check `BluetoothService` | `dotnet build BuzzahBuddy/BuzzahBuddy.csproj -f net10.0 -p:TargetFrameworks=net10.0` |
| Regenerate design tokens | `python3 scripts/gen_design_tokens.py` |
| Regenerate fonts / Lucide icons | `scripts/gen_fonts.py`, `scripts/gen_icons.py <lucide-static@1.55.0 dir>` (see docstrings) |

`-p:TargetFrameworks=...` is required on a single-TFM build: an unpinned restore resolves
every TFM and fails where a workload is missing (NETSDK1147). Targets:
`net10.0;net10.0-android;net10.0-ios` (iOS 15+, Android API 24 floor / 36 target). The
plain `net10.0` TFM is a Library that exists only for the test compile.

A plain Debug Android build uses Fast Deployment: its APK carries no app assemblies, which
`dotnet run` pushes separately. Installed by hand with `adb install`, it either keeps running
an older copy's assemblies or, on a clean install, crashes at launch. Add the two flags in
the table above for an APK that stands alone.
`Platforms/MacCatalyst|Tizen|Windows/` are template stubs no TFM builds. `.vscode/*.json`
are fully commented out.

## BLE Protocol — `Services/Bluetooth/`

Nordic UART Service; UUIDs and the `"BlueBuzzah"` name filter in `BlueBuzzahConstants.cs`.
Every BLE command ends in `\x04`. The next command goes out as soon as the previous reply
arrives (firmware queues 4 deep), except after a reply without a `CMD` line (old firmware):
the slot stays open `LegacyReplySettleMs` (100 ms). `ConnectionTimeoutSeconds = 15`
bounds link-up through subscribe — iOS never times out a connect itself.

- **No correlation IDs.** Current firmware starts each reply with `CMD:<COMMAND>`;
  `ResponseMatcher` completes the pending command only on an exact match and drops stale
  frames. Old firmware has no echo, so the fallback is `ResponseMatcher.ExpectedResponseKeys`
  (e.g. `PING→PONG`); a command without an entry accepts *any* frame. **Adding a command
  means adding its key**, or replies shift and desync.
- **One command in flight** (`BluetoothService._responseLock`); no priority queue.
- `IDENTIFY:PHONE` is written right after notification subscribe (connect *and*
  reconnect); firmware classifies the link within 1 s. App `Connected` is raised only
  after NUS discovery + `IDENTIFY:PHONE`, never from the adapter's `OnDeviceConnected`.
- **Two disconnects.** `DisconnectAsync`/`RaiseManualConnectFailure` set
  `UserInitiatedDisconnect` (blocks auto-reconnect, beats `ExpectingReboot`). App-initiated
  drops (reboot, failed health check) must use `DisconnectForReconnectAsync`.
- **Launch auto-connect** (`AutoConnectService`; Settings switch, Preferences `AutoConnect`,
  default on): `App` makes one `ConnectToKnownDeviceAsync` attempt per launch for
  `last_device`; Session and Gloves banners show it (Cancel while connecting, Try Again on
  failure). `_lastConnectedDeviceId` is set only on success: set earlier, a failed launch
  connect starts `ReconnectionService`'s loop. Demo Mode never saves `last_device`.
- `PROFILE_LOAD` and `RESTART` **reboot the glove**. `GloveControlService` sets
  `ExpectingReboot` (`ExpectingRebootTtl` 30 s) before sending and disconnects on
  `STATUS:REBOOTING`; `ReconnectionService` waits `RebootInitialDelayMs` (3 s), then backs
  off 1→2→4→8→16→30 s, `MaxReconnectAttempts` 20.
- `GloveControlService` is the **single post-(re)connect sync owner**: `INFO` →
  `SESSION_STATUS` → `THERAPY_LED_OFF` on a 0/2/5/10 s retry ladder (the primary is busy
  re-syncing with the secondary). VMs listen to `DeviceInfoReceived` /
  `TherapyLedOffReceived` — don't add per-VM commands on `Connected`; page refreshes pass
  `maxAge` to `GetDeviceInfoAsync`/`GetTherapyLedOffAsync` (joins in-flight, caches per
  link). An `INFO` missing the secondary battery is re-asked at 3/5/10/30 s (30 s repeats),
  skipping turns during a session.
- `RxFrameAssembler` reassembles `\x04` frames, drops primary↔secondary chatter
  (`InternalMessagePrefixes`), and caps a partial at `MaxPartialFrameLength` (4096).
- `PROFILE_CUSTOM` takes ≤ 8 KEY:VAL pairs, 63-char tokens, 255-char command; firmware
  silently drops overflow yet replies `CUSTOM_LOADED`, so `SetCustomProfileAsync`
  validates and callers send 8-pair chunks.
- Amplitude writes **widen before narrowing** (firmware rejects a narrowing write):
  `GloveControlService.AmplitudeParametersWideningFirst`; `ApplyCustomProfileAsync` floors
  `AMPMIN` first.
- Hardware "v2"/"v3" comes from advertisement manufacturer data (`ParseHardwareVersion`);
  null on older firmware.

## Cross-Repo Contract (Firmware) — by convention, no CI check

| App | Firmware |
|-----|----------|
| `Models/TherapyParameterBounds.cs` (+ range labels in `ProfileSettingsPage.xaml`) | `include/config.h` `PARAM_*` |
| `GloveControlService` custom-profile limits | `include/menu_controller.h` `MAX_COMMAND_PARAMS` |
| parameter validation / amplitude ordering | `src/profile_manager.cpp` `setParameter` |
| `RxFrameAssembler.InternalMessagePrefixes` | `include/internal_messages.h` `INTERNAL_MESSAGES` |
| `LastSessionSummary` (`LAST_*`), `SessionStatus.SessionId` / `PausedSeconds` | `src/menu_controller.cpp` `handleSessionStatus`, `SessionEndReason` in `include/therapy_engine.h` |

## Domain Models — `Models/`

- `TherapyParameterBounds` — firmware envelope; `EffectiveJitterCap()` mirrors the
  firmware's jitter clamp so the UI warns before silent capping.
- `ResearchDefaults` — the one clinically validated set (Pfeifer et al. 2021). **A
  permitted value is not a validated one**; UI copy says so.
- `SessionClock` — interpolates between `SESSION_STATUS` anchors; device time wins;
  backward drift ≤ `SnapThresholdSeconds` (3) is absorbed.
- `TherapyProfile` — presets 1–6; `CustomProfileId = 4` is the only editable slot. Custom
  is edited and saved on the phone (`IDataStorageService`), then `SendSavedCustomProfileAsync`
  sends it on Apply — after the reboot when Apply also loads Custom, since firmware takes
  `PROFILE_CUSTOM` only while Custom is loaded.
- `SessionRecord` / `OpenSession` — a saved session and the one being tracked. A session's
  end is never reported live (the next reading is IDLE 0/0/0); current firmware adds its last
  session's `LAST_*` summary to idle `SESSION_STATUS` replies, and `OpenSession.End` uses it
  when `LAST_ID` matches the `SESSION_ID` the session was tracked under. Otherwise (older
  firmware, a summary of another session) the end is inferred: completed when the last running
  reading plus the 15 s tolerance (`CompletionToleranceSeconds`) reaches the planned length, but
  only on firmware without `PAUSED`; a pause-free clock can't prove completion by timing, so it
  counts as ended early. Firmware that sends `PAUSED` (`OpenSession.PauseFreeClock`) holds ELAPSED
  while paused, so every ELAPSED gain is delivered time; older firmware's clock runs through pauses
  (`SESSION_ID` alone doesn't mark the new clock), so only gains after a running reading count. A
  matching session id continues a session however long the phone was away. Under 60 s
  (`MinimumSavedSeconds`) isn't saved.
- `BatteryReading` — `≤ 0.0 V` is the firmware's "no reading" sentinel → unavailable, not
  0%. 3.0 V = 0%, 4.2 V = 100%, linear.

## Testing

`BuzzahBuddy.Tests` is plain `net10.0` with **no `ProjectReference`** (it would restore
every app TFM and fail without mobile workloads); it `Compile Include`s app sources —
`Models/**`, `Services/Glove/**`, selected `Services/Bluetooth` and `Helpers` files, and
`Services/Storage/HistoryStore.cs` + `StorageJsonContext.cs`, and
`Services/Reports/ReportPdf.cs`; the report's five TTFs and the mark are linked as files
under `ReportAssets/` in the test output. A
new testable source needs its own `Compile Include` in `BuzzahBuddy.Tests.csproj`.

- `BluetoothService.cs` is **excluded**: its ctor hits `CrossBluetoothLE.Current`, which
  throws off-device. Only the `net10.0` compile check covers it.
- `TestHelpers/FakeBluetoothService.cs` is hand-rolled. **Do not add Moq.**
- `MockParityTests` asserts the mock matches the *firmware wire format* (raw `"PONG:"`),
  not app parsing, which normalizes and would hide drift.

**Demo Mode** (Settings) swaps in `MockBluetoothService` at runtime: `IBluetoothService`
is `SwitchableBluetoothService`, which routes to the real or mock service and forwards
only the active one's events (persisted as `demo_mode`). The mock is 4-motor only and
raises events synchronously on the caller's thread, so it hides a missing `MainThread`
dispatch.

## UI & Navigation

Tabs in `AppShell.xaml`: `//control` (Session), `//devices` (Gloves), `//history`
(History), `//settings`; pushed routes `calibration` (Test Motors) and `profilesettings`
(Custom Profile) in `AppShell.xaml.cs`; strings in `Helpers/Routes.cs`. Modals created
with `new`, not routes:
`ApplyingSettingsPage` (while gloves reboot), `WelcomePage` (first-run safety notices),
`ConfirmSheetPage` (every confirmation, via `IConfirmationService`).
`ApplyingSettingsPage.OnBackButtonPressed` only works on Android because
`AndroidManifest.xml` sets `enableOnBackInvokedCallback="false"`.
`DeviceSettingsViewModel` has no page; it's `DeviceListViewModel.Settings`.
`HistoryViewModel` reloads from `HistoryStore` on `OnAppearing` and never subscribes to
service events; in Demo Mode it shows `DemoHistory`'s 60-day sample instead (never stored),
and the shared files are named "Sample" and marked as sample data. History's one period
control (`SelectedRange`) drives both the overview and the shared report.

DI (`MauiProgram.cs`): tab pages/VMs are **singletons** (Shell caches them) and never
disposed; Calibration/ProfileSettings are transient — a transient VM subscribing to
service events must be disposed (see `CalibrationPage.OnPageUnloaded`). VMs use
`[ObservableProperty]` on `_camelCase` fields, `.SafeFireAndForget("[TAG]")` (not `_ =`),
and `MainThread.BeginInvokeOnMainThread` in service-event handlers.

- **Accessibility:** rules are the design system's `accessibility.md`. Every interactive
  element needs `SemanticProperties.Description` and an `AutomationId`; honor
  `Motion.Reduce` (`Helpers/Motion.cs`). Test with VoiceOver and TalkBack.
- **Large text:** `OsAccessibility.StackForLargeText` (OS scale ≥ 1.35) is the one
  threshold where side-by-side layouts stack; wire it with
  `view.TrackTextScale(update)`, which re-runs on load and window resume. Icons inside
  fixed shapes use the `IconInShape` style (no font scaling). On iOS, Display-group type
  styles carry `Typography.Heading` and scale on Apple's Large Title curve, never below
  body; Android 14+ already scales large text less.
- **Safe area:** every `ContentPage` sets `SafeAreaEdges="Container"` (.NET 10 defaults
  to `None`; Android 16 forces edge-to-edge, under the gesture bar). `SoftInput` unused.

### Tokens and styles

`scripts/gen_design_tokens.py` writes `Resources/Styles/Themes/*Theme.g.xaml(.cs)` (four
themes), `Tokens.g.xaml`, `Type.g.xaml`, `Helpers/DesignColors.g.cs` (for code without
app resources; `ColorResources.Get` falls back to dark) and
`Platforms/Android/Resources/values{,-night}/colors.xml`. **Never hand-edit `*.g.*`**:
rerun the script and commit. `scripts/gen_fonts.py` makes `Resources/Fonts/*.ttf` (static
weights; MAUI can't use variable woff2). Styles pick fonts via `FontDisplay500`-style keys
that `Helpers/Typography` repoints to heavier files under Bold Text.

**`LineHeight` multiplies the font's natural line height** (Lexend 1.25, Atkinson 1.30 em),
not the font size as CSS does, so the generator divides each design-system ratio by it
(`NATURAL_LINE_HEIGHT`, floored at 1). iOS adds the surplus above each line, so a title
centered beside an icon, image or control, or the first line of a card that sits low,
takes `LineHeight="1"`. Lexend's own 0.3 em above its caps remains (the web shows it too).

**A visual state that changes a color needs `Normal`/`PointerOver` setters repeating the
style's `DynamicResource` colors** — otherwise leaving Pressed/Disabled restores a
snapshot and the button keeps the old theme after an in-app theme switch.
`MauiProgram.TintButtonIconsWithText` gives every button's `FontImageSource` icon the
button's current `TextColor`.

## Gotchas

- **iOS Release is full AOT; Debug hides it.** Two shipped builds crashed in Release only.
  `BuzzahBuddy.csproj` guards: `EnableAotAnalyzer` with `IL3050` as error, iOS-Release-only
  `Switch.System.Reflection.ForceInterpretedInvoke` (launch crash without it), `TrimMode`
  partial. Never set `IsAotCompatible` (implies `IsTrimmable`). CI builds Debug only —
  verify reflection-adjacent changes with `-c Release` on a device.
- **Every persisted type needs `[JsonSerializable]` in
  `Services/Storage/StorageJsonContext.cs`**; a missing entry throws at runtime on device.
- **History storage:** `HistoryStore` writes `history.json` in `FileSystem.AppDataDirectory`
  (temp file + move). The open session lives in Preferences `open_session`. A file that won't
  parse is renamed `history.json.corrupt-<timestamp>` and never overwritten. Legacy
  `therapy_sessions` is migrated once, then cleared.
- **Shared report** (`Services/Reports/ReportPdf.cs`): PDFsharp `XGraphics` draws two fixed
  pages from `HistoryReport.Build` (page 1 use, page 2 patterns, notes and legal text); every
  session and day note is in the CSV, so the PDF has no tables. Page 2's gaps tighten when
  long notes would reach the legal text; `Render_NothingOverflowsOrOverlaps` guards both
  pages. Its five TTFs and the mark are `EmbeddedResource`s (`BuzzahBuddy.Report.*`, linked as
  files in tests). `GlobalFontSettings.FontResolver` can be set once per process, so the first
  `ReportAssets` wins. The `System.Security.Cryptography.Pkcs` 10.x reference in both csproj
  files overrides PDFsharp's vulnerable 8.0.1; don't drop it.
- **`XC0023` is suppressed on purpose**: compiled `RelativeSource AncestorType` fails
  inside recycled `CollectionView` items on device; `DeviceListPage` uses
  `Source={x:Reference page}`. `XC0045`: intentional parent-VM `RelativeSource`.
- `MauiProgram.StripNativeInputChrome` (Android) clears `Entry`/`Editor`/`Picker`
  backgrounds — the Material underline reads as a second border.
- **App icons come from the design system's `assets/`**: iOS `MauiIcon`
  (`Resources/AppIcon/appicon.png`, iOS only); Android adaptive mipmaps in
  `Platforms/Android/Resources/mipmap-*`. After replacing one, delete
  `obj/**/resizetizer` — it keeps old art under the same name.
- **Firmware update notice:** `Services/Firmware/LatestFirmware` is the app's only network
  call (GitHub `releases/latest`, cached a day in Preferences, silent on failure).
  `DeviceSettingsViewModel` compares it with INFO's `FW` (`Models/FirmwareRelease`) and shows
  the lowest-priority Gloves banner plus an "Outdated" badge. Skipped in Demo Mode, whose
  gloves report 2.0.0.
- `tmp/` is gitignored scratch, not canonical. No `docs/` tree; don't add one.

## CI — `.github/workflows/`

`build.yml` (PRs, pushes to `main` under `BuzzahBuddy*`): tests on ubuntu with no
workload, Android build + `net10.0` compile check, iOS build on macOS — all Debug. Both
workflows pin `xcode-select` and the iOS workload version together (each workload binds
one Xcode; unpinned → MT0180); bump them in lockstep.

`release.yml` (GitHub Release **published**): tag must be `vX[.Y[.Z]]` (App Store Connect
rejects anything else). Version comes from the tag; csproj `ApplicationDisplayVersion` is
ignored. Build number = `github.run_number + BUILD_OFFSET` (2); **raise `BUILD_OFFSET`
after any manual store upload** — both stores reject a build number ≤ one already
accepted. Ships to TestFlight and Play internal.
