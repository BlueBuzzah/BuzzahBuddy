# BuzzahBuddy

.NET MAUI companion app for the BlueBuzzah vibrotactile therapy gloves (Parkinson's
disease). Talks BLE to the gloves to run therapy sessions, tune parameters, and
calibrate motors. Mission: **highly accessible, tremor-friendly UI** — that constraint
outranks visual preference in every design call.

Sibling repos in the same workspace: `../BlueBuzzah-Firmware` (device firmware — the
BLE contract lives there), `../BlueBuzzah-Updater`, `../BlueBuzzah-Hardware`.

## Design System — required for every UI/UX change

Any change a person can see, hear or touch follows the **BlueBuzzah design system**. That
includes layout, color, type, spacing, controls, icons, the app icon and splash, motion,
haptics, accessibility, and copy (voice, error messages, the disclaimer). The design
system lives in the BlueBuzzah workspace repo as the `bluebuzzah-design-system` skill, at
`../../.claude/skills/bluebuzzah-design-system/` from this repo's root.

Before making a UI/UX change:

1. **Load it.** Invoke the `bluebuzzah-design-system` skill. If it isn't in your skill list
   (e.g. Claude was started in this repo instead of the workspace root), read
   `../../.claude/skills/bluebuzzah-design-system/SKILL.md` directly and follow it.
2. **If that path doesn't exist, stop.** This repo isn't checked out inside the workspace.
   Ask the user to set it up: clone `https://github.com/BlueBuzzah/BlueBuzzah-Workspace`,
   then run `npm run workspace:setup` to clone this repo under its `projects/`. Don't work
   from memory.
3. **Read what applies:** `README.md` and `accessibility.md`, then `buzzahbuddy.md` (this
   app's surface guide), then the README of every design-system component you rebuild.

Rules:

- **The design system wins any conflict.** That includes the current
  `Styles.xaml` and anything else in this file. Much of the current UI
  predates it. Build new and changed UI to the design system rather than copying the
  surrounding legacy styles.
- **It ships no XAML.** Its React components are the reference for each control's
  anatomy, states and rules. Build them as MAUI controls and styles. Derive XAML
  resources from its `tokens.json` (all four themes) rather than hand-typing hex values,
  sizes or fonts.
- **Copy, don't link.** Copy fonts, icons and the app-icon files from the design system's
  `assets/` (`iOS`, `Android`) into this repo. Never reference the workspace path at build
  time, because this repo builds and ships on its own.
- **Title Case labels (owner override of the design system's sentence case).** Buttons,
  screen, card, sheet, alert and banner titles, tab names, field and switch labels,
  segment options, badges and profile names use Title Case, with articles, conjunctions
  and prepositions lowercase mid-title ("Scan for Gloves", "Skip the Motor Test?").
  Messages, hints, errors and screen-reader announcements stay sentence case; a message
  that names a button uses its label as written ("tap Apply Settings").
- **Pushed screens title the nav bar (owner override of the design system's in-page
  headline).** Screens with a Back bar (Test Motors, Custom Profile) set the view model's
  `Title` and have no headline in the page. Tab screens hide the bar and keep the headline.
- **Don't invent rules.** If the design system doesn't cover something, ask rather than
  inventing a rule.
- **Full migrations:** a whole-app pass is the `/implement-design-system` command, run from
  the workspace.

## Quick Reference

| Task | Command |
|------|---------|
| Test | `dotnet test BuzzahBuddy.Tests` |
| Build Android | `dotnet build BuzzahBuddy/BuzzahBuddy.csproj -f net10.0-android -p:TargetFrameworks=net10.0-android` |
| Build iOS | `dotnet build BuzzahBuddy/BuzzahBuddy.csproj -f net10.0-ios -p:TargetFrameworks=net10.0-ios` |
| Compile-check `BluetoothService` | `dotnet build BuzzahBuddy/BuzzahBuddy.csproj -f net10.0 -p:TargetFrameworks=net10.0` |
| Run on physical iPhone | `./run-ios.sh` (env: `IOS_UDID`, `IOS_PROVISION`, `IOS_CONFIG`) |
| Regenerate design tokens | `python3 scripts/gen_design_tokens.py` |
| Regenerate fonts | `scripts/gen_fonts.py` (needs fontTools + brotli; see its docstring) |
| Regenerate Lucide icons | `scripts/gen_icons.py <lucide-static@1.55.0 package dir>` (see its docstring) |

`-p:TargetFrameworks=...` is not optional on a single-TFM build: an unpinned restore
resolves assets for *all* TFMs and fails wherever that workload isn't installed
(NETSDK1147). README's build/test commands omit it — trust this table over README.
`.vscode/tasks.json` and `launch.json` are entirely commented out.

Targets: `net10.0;net10.0-android;net10.0-ios` (iOS 15+, Android API 24 floor / 36
target). `Platforms/MacCatalyst|Tizen|Windows/` are template stubs **not built by any
TFM** — a `-f net10.0-maccatalyst` build fails. The plain `net10.0` TFM exists only so
the test project can compile app sources; it builds as a Library.

## BLE Protocol — `Services/Bluetooth/`

Nordic UART Service. Constants in `BlueBuzzahConstants.cs`: service
`6E400001-B5A3-F393-E0A9-E50E24DCCA9E`, TX `...0002...`, RX `...0003...`; device name
filter `"BlueBuzzah"`; every BLE command terminates in `\x04` (serial/debug uses `\n`).
No inter-command delay: the next command goes out as soon as the previous reply arrives
(firmware queues 4 deep). Exception: after a reply without a `CMD` line (older firmware)
the reply slot stays open `LegacyReplySettleMs` (100 ms) so a stray frame can't complete
the next command. `ConnectionTimeoutSeconds = 15` bounds link-up through subscribe —
iOS never times out a connect on its own.

- **No correlation IDs.** Current firmware starts every reply with `CMD:<COMMAND>`;
  `ResponseMatcher` completes the pending command only on an exact CMD match and drops
  other frames as stale. Older firmware has no echo, so the fallback is
  `ResponseMatcher.ExpectedResponseKeys` — a key the reply must contain (e.g.
  `INFO→ROLE`, `PING→PONG`). Commands with no entry accept *any* frame on old firmware;
  adding a command means adding its key, or responses shift and desync.
- **One command in flight**, serialized by `BluetoothService._responseLock`
  (`SemaphoreSlim(1,1)`). There is no priority queue.
- `IDENTIFY:PHONE` is written right after notification subscribe (fresh connect *and*
  reconnect) — firmware classifies the link within a 1s window and mistypes it otherwise.
- Adapter `OnDeviceConnected` deliberately does **not** raise app `Connected`; it fires
  before GATT discovery. `Connected` is raised only after NUS discovery + `IDENTIFY:PHONE`.
- **Two disconnects.** `DisconnectAsync` (and `RaiseManualConnectFailure`) set
  `UserInitiatedDisconnect`, which blocks auto-reconnect and always beats
  `ExpectingReboot`. App-initiated drops (reboot, failed health check) must use
  `DisconnectForReconnectAsync`.
- `PROFILE_LOAD` and `RESTART` **reboot the device**. `GloveControlService` sets
  `ExpectingReboot` (30s `ExpectingRebootTtl`) *before* sending and disconnects at once on
  `STATUS:REBOOTING` instead of waiting ~300 ms for the glove to drop the link;
  `ReconnectionService` waits `RebootInitialDelayMs = 3000`, then backs off
  1→2→4→8→16→30s, 20 attempts max.
- `GloveControlService` is the **single post-(re)connect sync owner**: `INFO` →
  `SESSION_STATUS` → `THERAPY_LED_OFF`, retried on a `{0, 2000, 5000, 10000}ms` ladder
  (the primary is re-syncing with the secondary, ~5s cold, and starves early commands).
  VMs take battery via `DeviceInfoReceived` and the LED via `TherapyLedOffReceived` —
  don't add per-VM commands on `Connected`. For page-visit refreshes pass a `maxAge` to
  `GetDeviceInfoAsync`/`GetTherapyLedOffAsync`: the service joins an in-flight query and
  caches per link instead of each VM keeping its own freshness stamp. An `INFO` with no
  secondary battery (it hasn't rejoined the primary yet) re-sends `INFO` at 3→5→10→30s
  (30s repeats) until it arrives or the link drops, skipping turns during a session.
- `RxFrameAssembler` reassembles `\x04`-terminated frames across packets and drops
  firmware-internal primary↔secondary chatter (`InternalMessagePrefixes`).
  `MaxPartialFrameLength = 4096` caps a runaway partial.
- `PROFILE_CUSTOM` takes at most **8 KEY:VAL pairs**, 63-char tokens, 255-char command.
  Firmware silently drops the overflow and still replies `CUSTOM_LOADED`, so the app
  validates client-side.
- Amplitude writes must **widen before narrowing** — firmware rejects a narrowing write
  against the current window. `GloveControlService.AmplitudeParametersWideningFirst`
  handles ordering; `ApplyCustomProfileAsync` first floors `AMPMIN`.
- Hardware generation ("v2"/"v3") comes from advertisement manufacturer data
  (`ParseHardwareVersion`, firmware `BLE_MFG_DATA_INIT`); null on older firmware.

## Cross-Repo Contract (Firmware)

Maintained **by convention only — no CI check diffs these.** When touching either side,
check both:

| App | Firmware |
|-----|----------|
| `Models/TherapyParameterBounds.cs` (+ hardcoded ranges in `ProfileSettingsPage.xaml` labels) | `include/config.h` `PARAM_*` |
| `GloveControlService` custom-profile limits | `include/menu_controller.h` `MAX_COMMAND_PARAMS` |
| parameter validation / amplitude ordering | `src/profile_manager.cpp` `setParameter` |
| `RxFrameAssembler.InternalMessagePrefixes` | `include/internal_messages.h` `INTERNAL_MESSAGES` |

## Domain Models — `Models/`

- `TherapyParameterBounds` — the firmware envelope, plus `EffectiveJitterCap()` which
  re-implements the firmware's jitter clamp so the UI can warn *before* silent capping.
- `ResearchDefaults` — the one clinically validated parameter set (Pfeifer et al. 2021).
  The bounds are an engineering envelope around that single point: **a permitted value is
  not a validated one.** Say so in UI copy.
- `SessionClock` — timer-free interpolation between `SESSION_STATUS` anchors. Device time
  is authoritative; backward drift ≤ `SnapThresholdSeconds = 3` is absorbed, not shown.
- `TherapyProfile` — 6 presets, IDs 1–6; `CustomProfileId = 4` is the only editable slot.
  The editor saves Custom settings on the phone (`IDataStorageService`), no glove needed;
  Apply sends them with `SendSavedCustomProfileAsync`, after the reboot when it also loads
  Custom, because the firmware takes `PROFILE_CUSTOM` only while Custom is loaded.
- `BatteryReading` — `≤0.0V` is the firmware's "no reading" sentinel, surfaced as
  unavailable, not 0%. 3.0V=0% / 4.2V=100% linear.

## Testing

`BuzzahBuddy.Tests` is plain `net10.0` with **no `ProjectReference`** — it
`Compile Include`s app sources (`Models/**`, `Services/Glove/**`, `RxFrameAssembler`,
`ResponseMatcher`, `BlueBuzzahConstants`, `IBluetoothService`, `MockBluetoothService`,
`SwitchableBluetoothService`,
`DesignColors.g`, `LucideIcons.g`, `RepeatGuard`, `CubicBezier`). A ProjectReference would force restore across every app TFM
and fail without mobile workloads. A new testable source needs its own `Compile Include`.

- `BluetoothService.cs` is **excluded on purpose** — its ctor hits
  `CrossBluetoothLE.Current`, which throws off-device. Its only coverage is the `net10.0`
  compile check in CI.
- `TestHelpers/FakeBluetoothService.cs` is hand-rolled. **Do not add Moq.**
- `MockParityTests` asserts the mock matches *firmware wire format*, not app contracts —
  e.g. raw `"PONG:"` pre-parse, because `CommandResponse.Parse` normalizes both forms and
  would hide a mock drift.

`MockBluetoothService` (pretend gloves) is switched on at runtime by **Settings > Demo
Mode**: `IBluetoothService` is `SwitchableBluetoothService`, which routes every call to
the real or mock service and forwards only the active one's events (persisted under
`demo_mode`). It simulates a 4-motor board only, and raises events synchronously on the
caller's thread, so it hides a missing `MainThread` dispatch (real BLE events arrive off
the UI thread).

## UI & Navigation

3 tabs in `AppShell.xaml` — `//control`, `//devices`, `//settings`; pushed routes
`calibration` and `profilesettings` registered in `AppShell.xaml.cs`; strings in
`Helpers/Routes.cs`. `ApplyingSettingsPage` is a **modal** (`PushModalAsync`, created with
`new`, not a route) shown while the gloves reboot; its `OnBackButtonPressed` block works
on Android only because `AndroidManifest.xml` sets `enableOnBackInvokedCallback="false"`.
`DeviceSettingsViewModel` has no page of its own: it's `DeviceListViewModel.Settings`.
`WelcomePage` (first-run safety notices) and `ConfirmSheetPage` (every confirmation, via
`IConfirmationService`) are modals too.

DI (`MauiProgram.cs`): tab-backing pages/VMs are **singletons** (Shell caches TabBar
content) and never disposed; `Calibration`/`ProfileSettings` are transient. A transient VM
that subscribes to service events must be disposed like `CalibrationPage.OnPageUnloaded`.

VM conventions: `[ObservableProperty]` on `_camelCase` fields; fire-and-forget via
`.SafeFireAndForget("[TAG]")`, not `_ =`; service-event handlers marshal with
`MainThread.BeginInvokeOnMainThread`.

### Accessibility (non-negotiable)

The rules (targets, gestures, contrast, motion, screen readers) are the design system's
`accessibility.md`. MAUI specifics: every interactive element needs
`SemanticProperties.Description` and an `AutomationId`; `Helpers/Motion.cs` —
`Motion.Reduce` reads the OS reduce-motion preference; honor it. Test with VoiceOver and
TalkBack.

### Safe area

**Every `ContentPage` must set `SafeAreaEdges="Container"`** (all 8 do). .NET 10 changed
the default to `None` and Android 16 forces edge-to-edge; `DeviceListPage` anchors its
primary button to the bottom edge, exactly where the gesture nav bar sits. `SoftInput` is
intentionally unused.

### Colors and tokens

`scripts/gen_design_tokens.py` generates every color and size token from the design
system's `tokens.json` (stdlib Python; `--ds` defaults to the workspace skill path). It
writes `Resources/Styles/Themes/*Theme.g.xaml(.cs)` (the four themes), `Tokens.g.xaml`,
`Type.g.xaml`, `Helpers/DesignColors.g.cs` (the same colors for code that can't reach app
resources; `ColorResources.Get` falls back to its dark theme) and
`Platforms/Android/Resources/values{,-night}/colors.xml`. **Never hand-edit them**: rerun
the script after the design system changes and commit the output. `scripts/gen_fonts.py`
likewise makes `Resources/Fonts/*.ttf` (static weights; MAUI can't use the design
system's variable woff2), registered in `MauiProgram.ConfigureFonts`. Styles pick fonts
through `FontDisplay500`-style resource keys that `Helpers/Typography` points at heavier
files under Bold Text.

**A visual state that changes a color must be paired with `Normal`/`PointerOver` setters
repeating the style's `DynamicResource` colors.** Leaving Pressed or Disabled otherwise
restores a fixed snapshot, so after an in-app theme switch the button keeps the old
theme's colors. A button icon's own color is overridden: `MauiProgram.TintButtonIconsWithText`
gives every `FontImageSource` icon the button's current `TextColor`.

## Gotchas

- **iOS Release is full AOT; Debug hides it.** Two shipped builds crashed on device in
  Release only. Guards in `BuzzahBuddy.csproj`: `EnableAotAnalyzer` with `IL3050` as an
  error, and the iOS-Release-only `Switch.System.Reflection.ForceInterpretedInvoke`
  (launch crash without it). Never set `IsAotCompatible` (it implies `IsTrimmable`).
  `build.yml` builds Debug only — verify reflection-adjacent changes with `-c Release` on
  a device.
- **Every persisted type needs a `[JsonSerializable]` in
  `Services/Storage/StorageJsonContext.cs`.** A missing entry throws at runtime on
  device, not at compile time. Keep `TrimMode=partial`.
- **`XC0023` is suppressed for a reason.** Compiled `RelativeSource AncestorType` silently
  fails to resolve inside recycled `CollectionView` item containers *on device* (dead
  Connect button); `DeviceListPage` uses `Source={x:Reference page}` instead.
  `BindableLayout` (`CalibrationPage`) is unaffected. `XC0045`: intentional parent-VM
  `RelativeSource`.
- `MauiProgram.StripNativeInputChrome` (Android) clears `Entry`/`Editor`/`Picker` platform
  backgrounds — the Material underline reads as a second border inside `TextFieldBorder`.
- **App icons come from the design system's `assets/`.** iOS uses `MauiIcon`
  (`Resources/AppIcon/appicon.png`, iOS only); Android uses the adaptive mipmaps in
  `Platforms/Android/Resources/mipmap-*`, not `MauiIcon`. After replacing an icon, delete
  `obj/**/resizetizer`: the resizetizer keeps the old art under the same file name.
- `tmp/` is gitignored scratch; nothing in it is canonical.
- **No `docs/` tree, and don't add one**: design rules come from the workspace design
  system (top of this file).

## CI — `.github/workflows/`

`build.yml` (PR, push to `main` under `BuzzahBuddy*` paths): test on ubuntu without any
workload, Android build + the `net10.0` compile check, iOS build on macOS — all Debug.

The iOS jobs (both workflows) pin `xcode-select` and the workload set together
(`dotnet workload install maui-ios --version …`): each iOS workload binds one Xcode,
and an unpinned install picks up iOS SDKs newer than the runner's Xcode (MT0180).
Bump both in lockstep.

`release.yml` (on GitHub Release **published** only): tag must be `vX[.Y[.Z]]` — anything
else (`v1.0.0-beta1`) is rejected by App Store Connect at upload, so the job fails fast.
Version comes from the tag (csproj `ApplicationDisplayVersion` is ignored). Build number =
`github.run_number + BUILD_OFFSET` (currently `2`); **raise `BUILD_OFFSET` after any
manual out-of-band store upload**, since both stores permanently reject a build number ≤
one already accepted. Signs and ships to TestFlight and Play internal.
