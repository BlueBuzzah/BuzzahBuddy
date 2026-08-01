# BuzzahBuddy

.NET MAUI companion app for the BlueBuzzah vibrotactile therapy gloves (Parkinson's
disease). Talks BLE to the gloves to run therapy sessions, tune parameters, and
calibrate motors. Mission: **highly accessible, tremor-friendly UI** — that constraint
outranks visual preference in every design call.

Sibling repos in the same workspace: `../BlueBuzzah-Firmware` (device firmware — the
BLE contract lives there), `../BlueBuzzah-Updater`, `../BlueBuzzah-Hardware`.

## Quick Reference

| Task | Command |
|------|---------|
| Test | `dotnet test BuzzahBuddy.Tests` |
| Build Android | `dotnet build BuzzahBuddy/BuzzahBuddy.csproj -f net10.0-android -p:TargetFrameworks=net10.0-android` |
| Build iOS | `dotnet build BuzzahBuddy/BuzzahBuddy.csproj -f net10.0-ios -p:TargetFrameworks=net10.0-ios` |
| Compile-check `BluetoothService` | `dotnet build BuzzahBuddy/BuzzahBuddy.csproj -f net10.0 -p:TargetFrameworks=net10.0` |
| Run on physical iPhone | `./run-ios.sh` (env: `IOS_UDID`, `IOS_PROVISION`, `IOS_CONFIG`) |

`-p:TargetFrameworks=...` is not optional on a single-TFM build: an unpinned restore
resolves assets for *all* TFMs and fails wherever that workload isn't installed
(NETSDK1147). See README.md for full environment setup. `.vscode/tasks.json` and
`launch.json` are entirely commented out — dead, not usable as written.

Targets: `net10.0;net10.0-android;net10.0-ios` (iOS 15+, Android API 24 floor / 36
target). `Platforms/MacCatalyst|Tizen|Windows/` are template stubs **not built by any
TFM** — a `-f net10.0-maccatalyst` build fails. The plain `net10.0` TFM exists only so
the test project can compile app sources; it builds as a Library.

## BLE Protocol — `Services/Bluetooth/`

Nordic UART Service. Constants in `BlueBuzzahConstants.cs`: service
`6E400001-B5A3-F393-E0A9-E50E24DCCA9E`, TX `...0002...`, RX `...0003...`; device name
filter `"BlueBuzzah"`; every BLE command terminates in `\x04` (serial/debug uses `\n`);
`CommandDelayMs = 100` between commands.

Non-obvious invariants:

- **No correlation IDs.** `BluetoothService.ExpectedResponseKeys` maps each command
  prefix to a key the reply must contain (`INFO→ROLE`, `BATTERY→BATP`, `PING→PONG`);
  frames missing it are discarded as stale. Adding a command means adding its key, or
  responses shift and desync.
- **One command in flight**, serialized by `BluetoothService._responseLock`
  (`SemaphoreSlim(1,1)`). There is no priority queue.
- `IDENTIFY:PHONE` is written right after notification subscribe (fresh connect *and*
  reconnect) — firmware classifies the link within a 1s window and mistypes it otherwise.
- Adapter `OnDeviceConnected` deliberately does **not** raise app `Connected` state; it
  fires before GATT discovery, so commands sent then fail. `Connected` is raised only
  after NUS discovery + `IDENTIFY:PHONE`.
- `PROFILE_LOAD` **reboots the device**. `GloveControlService.LoadProfileAsync` sets
  `ExpectingReboot` *before* sending; `ReconnectionService` then waits
  `RebootInitialDelayMs = 3000` before its first retry (backoff 1→2→4→8→16→30s, 20 max).
  `UserInitiatedDisconnect` always beats `ExpectingReboot` — never auto-reconnect after
  a user disconnect.
- Post-reconnect `INFO`/`SESSION_STATUS` retry on a `{0, 2000, 5000, 10000}ms` ladder:
  the primary is re-establishing clock sync with the secondary (~5s cold start) and
  starves early commands.
- `RxFrameAssembler` reassembles `\x04`-terminated frames across packets and drops
  firmware-internal primary↔secondary chatter (`InternalMessagePrefixes`, keyed to
  firmware `INTERNAL_MESSAGES`). `MaxPartialFrameLength = 4096` caps a runaway partial.
- `PROFILE_CUSTOM` takes at most **8 KEY:VAL pairs** (firmware `MAX_COMMAND_PARAMS=16`
  tokens, 63-char tokens, 255-char command). Firmware silently drops the overflow and
  still replies `CUSTOM_LOADED`, so the app validates client-side.
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
| `Models/TherapyParameterBounds.cs` | `include/config.h` `PARAM_*` |
| `GloveControlService` custom-profile limits | `menu_controller.h/.cpp` |
| parameter validation / amplitude ordering | `profile_manager.cpp` `setParameter` |
| `RxFrameAssembler.InternalMessagePrefixes` | `menu_controller.cpp` `INTERNAL_MESSAGES` |

A bounds mismatch means the app either sends values firmware rejects or blocks values it
would accept.

## Domain Models — `Models/`

- `TherapyParameterBounds` — the firmware envelope, plus `EffectiveJitterCap()` which
  re-implements the firmware's jitter clamp so the UI can warn *before* silent capping.
- `ResearchDefaults` — the one clinically validated parameter set (Pfeifer et al. 2021).
  The bounds are an engineering envelope around that single point: **a permitted value is
  not a validated one.** Say so in UI copy.
- `SessionClock` — timer-free interpolation between `SESSION_STATUS` anchors. Device time
  is always authoritative; backward drift ≤ `SnapThresholdSeconds = 3` is absorbed rather
  than shown as a jump.
- `TherapyProfile` — 6 presets, IDs 1–6; `CustomProfileId = 4` is the only editable slot.
- `BatteryReading` — `≤0.0V` is the firmware's "no reading" sentinel, surfaced as
  unavailable, not 0%. 3.0V=0% / 4.2V=100% linear.

## Testing

`BuzzahBuddy.Tests` is plain `net10.0` with **no `ProjectReference`** — it
`Compile Include`s app sources (`Models/**`, `Services/Glove/**`, `RxFrameAssembler`,
`BlueBuzzahConstants`, `IBluetoothService`, `MockBluetoothService`, `DesignColors`,
`IntMatch`). A ProjectReference would force restore across every app TFM and fail without
mobile workloads. Adding a testable app source means adding a `Compile Include`.

- `BluetoothService.cs` is **excluded on purpose** — its ctor hits
  `CrossBluetoothLE.Current`, which throws off-device. Its only coverage is the `net10.0`
  compile check in CI.
- `TestHelpers/FakeBluetoothService.cs` is hand-rolled. **Do not add Moq.**
- `MockParityTests` asserts the mock matches *firmware wire format*, not app contracts —
  e.g. raw `"PONG:"` pre-parse, because `CommandResponse.Parse` normalizes both forms and
  would hide a mock drift.

`MockBluetoothService` (dev without hardware) is selected by
`MauiProgram.UseMockBluetooth`, a hardcoded `false` — hand-edit and rebuild; it is not a
build flag. It simulates a 4-motor board only (no PentaBuzzer 5-motor coverage).

## UI & Navigation

3 tabs in `AppShell.xaml` — `//control`, `//devices`, `//settings`; pushed routes
`calibration` and `profilesettings` registered in `AppShell.xaml.cs`. Route strings live
in `Helpers/Routes.cs`. `ApplyingSettingsPage` is pushed directly (not a Shell route) —
it's the interstitial while the gloves reboot. `DeviceSettingsViewModel` has **no page of
its own**: it's a nested child VM (`DeviceListViewModel.Settings`) and the `x:DataType`
of `ApplyingSettingsPage`.

DI (`MauiProgram.cs`): tab-backing pages/VMs are **singletons** because Shell caches
TabBar content; `Calibration`/`ProfileSettings` pages and VMs are transient.

### Accessibility (non-negotiable)

WCAG 2.1 AA. Touch targets 48×48 minimum (56×56 preferred), 12–16pt spacing — larger than
the platform 44pt floor, for tremor. No hover-only interactions, no drag gestures, no
sliders for binary choices. Every interactive element needs
`SemanticProperties.Description` and an `AutomationId`. Test with VoiceOver and TalkBack.
`Helpers/Motion.cs` — `Motion.Reduce` reads the OS reduce-motion preference; honor it.
Full patterns: `DESIGN_GUIDE.md`.

### Safe area

**Every `ContentPage` must set `SafeAreaEdges="Container"`** (all 6 currently do). .NET 10
changed the default to `None` on all platforms and Android 16 forces edge-to-edge with no
opt-out; `DeviceListPage`/`CalibrationPage` anchor primary buttons to the bottom edge for
easy reach, exactly where the gesture nav bar sits. `SoftInput` is intentionally unused —
it has no effect on a bare `ScrollView` anyway.

### Colors

Source of truth is `Resources/Styles/Colors.xaml` (dark theme only; `Primary #35B6F2`,
`PageBackground #0a0a0a`, `CardBackground #05212D`). `Helpers/DesignColors.cs` duplicates
a subset as compile-time constants and is **kept in sync by hand** — change both.
`Helpers/ColorResources.cs` bridges them, falling back to the constants when app resources
aren't available (tests, early startup). Brand rule: **blue signals success, never green.**

## Gotchas

- **`TrimMode=partial` is load-bearing.** Android only sets
  `JsonSerializerIsReflectionEnabledByDefault=true` under `partial`;
  `PreferencesStorageService` uses reflection-based `System.Text.Json` with no
  `JsonSerializerContext`. Switching to `full` makes every save throw at runtime on
  Android. Add source-generated contexts first.
- **`XC0023` is suppressed for a reason.** `x:DataType={x:Null}` in item templates is
  deliberate — the compiled `RelativeSource AncestorType` alternative silently fails to
  resolve inside `CollectionView` item containers *on device*, producing a dead Connect
  button. Reproduces only on hardware. (`XC0022`: `Picker.ItemDisplayBinding` can't use
  compiled bindings; `XC0045`: intentional parent-VM `RelativeSource`.)
- `MauiProgram.StripNativeInputChrome` (Android) clears `Entry`/`Editor`/`Picker` platform
  backgrounds — `AppCompatEditText`'s Material underline reads as a second border inside
  the app's `InputBorder` wrapper.
- `tmp/` is gitignored scratch. `tmp/BLE_PROTOCOL.md` is a stale snapshot of the firmware
  spec — useful for diffing, never citable as canonical.
- **There is no `docs/` tree and none should be added.** The design system docs were
  deleted deliberately (`ae481f3`); their surviving rules — contrast matrix, forbidden
  pairs, motion spec — now live in `DESIGN_GUIDE.md`, and token values live in
  `Colors.xaml`/`Styles.xaml`. Don't reintroduce a parallel doc tree for either.

## CI — `.github/workflows/`

`build.yml` (PR, push to `main` under `BuzzahBuddy*` paths): test on ubuntu without any
workload, Android build + the `net10.0` compile check, iOS build on macOS.

`release.yml` (on GitHub Release **published** only): tag must be `vX[.Y[.Z]]` — anything
else (`v1.0.0-beta1`) is rejected by App Store Connect at upload, so the job fails fast.
Build number = `github.run_number + BUILD_OFFSET` (currently `2`); **raise `BUILD_OFFSET`
after any manual out-of-band store upload**, since both stores permanently reject a build
number ≤ one already accepted. Signs and ships to TestFlight and Play internal.
