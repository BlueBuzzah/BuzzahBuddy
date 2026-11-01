---
name: android-screenshots
description: Use when capturing or refreshing BuzzahBuddy Android Google Play screenshots, or when screens changed and the store images need retaking.
---

# Android Play Store Screenshots

Captures BuzzahBuddy in Demo Mode (no gloves needed) on one of two emulators. Always dark mode, for
now. Run everything from the repo root.

| Device | AVD | Shots |
|--------|-----|-------|
| `Phone` (default) | `Pixel_9_Pro` | 1280×2856 portrait |
| `Tablet` | `Tablet_Screenshots`, used only by this skill | 2560×1600 landscape (Play 10" tablet) |

## Needs

The .NET Android workload, the Android SDK (`ANDROID_HOME`, default `~/Library/Android/sdk`), and the
AVDs:

- `Pixel_9_Pro`: Android Studio > Device Manager > Pixel 9 Pro.
- `Tablet_Screenshots`: `avdmanager create avd -n Tablet_Screenshots -d medium_tablet -k "system-images;android-36;google_apis_playstore;arm64-v8a"`.
  The android-37 16 KB-page image hangs at boot headless on this Mac; use android-36.
- **Tablet build:** the tablet pass is only worth running on a build with tablet layouts. Pass its APK
  as the second argument.

## Steps

1. **Build** a standalone debug APK (takes minutes; run it in the background):
   `dotnet build BuzzahBuddy/BuzzahBuddy.csproj -f net10.0-android -p:TargetFrameworks=net10.0-android -p:EmbedAssembliesIntoApk=true`
2. **Check the shot list** (below) still fits the app. If a screen changed, edit `scripts/capture.sh`.
3. **Capture:** `bash .claude/skills/android-screenshots/scripts/capture.sh [Phone|Tablet] [path/to/app.apk]`
   boots the emulator headless if needed, turns the tablet to landscape, and writes
   `assets/screenshots/Android/<device>/YYYY-MM-DD/YYYY-MM-DD-NN-name.png`. It does a clean install, so
   the first-run notices are skipped and nothing from earlier runs shows.
4. **Review every PNG** with Read. Retake (`DEVICE=Tablet bash .claude/skills/android-screenshots/scripts/emu.sh shot <name>`;
   `DEVICE` defaults to Phone) anything with a dialog, keyboard, scroll bar, half-cut heading, loading
   state or Demo Mode banner in view.

## Shot List

Store order, the same five as the `ios-screenshots` skill. The tablet's two-pane screens show more
per shot (History pairs the overview or calendar with the days list).

| # | Name | Screen | Why |
|---|------|--------|-----|
| 01 | `session-running` | Session tab, session started | The core job: a therapy session with a clear timer and big Pause/Stop |
| 02 | `gloves` | Gloves tab, both connected | Both gloves, battery and the therapy profiles |
| 03 | `history-overview` | History, scrolled past the banner | Progress: total time, days used, sessions |
| 04 | `history-calendar` | History, last month's calendar in view | Daily use at a glance |
| 05 | `test-motors` | Test Motors, step 3, a few fingers checked | Setup confidence: every finger buzzes |

**Leave out:** Settings, Welcome, forms (Custom Profile), empty or disconnected states, confirm sheets,
and Gloves during a session (it shows a lock banner).

If `BuzzahBuddy/Models/DemoHistory.cs` doesn't exist, History is empty in Demo Mode: skip 03 and 04.

## Driving the Emulator by Hand

`scripts/emu.sh` (run with no arguments for usage): `tap <AutomationId>` (scrolls to it if needed),
`tab <title>`, `scroll <dp>` (from the top), `ids` (what's on screen), `shot <name>`, `back`. Positions
come from the screen size; History scrolls are per device in `capture.sh`.

## Gotchas

- **Use the SDK's adb.** `emu.sh` calls `$ANDROID_HOME/platform-tools/adb`. Another adb version on PATH
  (`/usr/local/bin/adb`) kills the shared server, VS Code's .NET MAUI extension restarts it, and every
  adb call fails with "protocol fault" until the other version stops. Anything else driving emulators
  on the machine must use the same adb.
- **Find emulators by AVD name, not serial;** serials shift as emulators start and stop.
- **The UI dump lists only views on screen,** so `tap` scrolls down until it finds the AutomationId
  (MAUI sets it as the view's `resource-id`).
- **Drag inside the column that scrolls.** `tap` drags at the element's own x; `scroll` drags at the
  tablet's left column. A drag between columns moves nothing.
- **`uiautomator dump` fails while the UI animates** ("null root node"); `emu.sh` retries.
- **Prefs are seeded with `run-as`** into `shared_prefs/com.rbonestell.buzzahbuddy_preferences.xml`
  before first launch. That needs a debug build.
- **`install -g` grants the Bluetooth permissions,** or the nearby-devices prompt covers the scan.
- **`monkey` aborts on a device without hardware keys** (the tablet) unless given `--pct-syskeys 0`.
- **Fast swipes fling.** `emu.sh scroll` drags slowly so a position repeats run to run; a distance
  taller than the screen becomes several drags.
- Pushed pages (Test Motors) show a bright blue status bar.
- Battery levels, firmware and History are Demo Mode sample data, the same every run.
