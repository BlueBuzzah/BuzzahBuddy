---
name: android-screenshots
description: Use when capturing or refreshing BuzzahBuddy Android Google Play screenshots, or when screens changed and the store images need retaking.
---

# Android Play Store Screenshots

Captures BuzzahBuddy on a **Pixel 9 Pro** emulator in Demo Mode (no gloves needed). Run everything from
the repo root.

## Needs

A `Pixel_9_Pro` AVD (Android Studio > Device Manager > Pixel 9 Pro), `adb` on PATH, `ANDROID_HOME` set, and
the .NET Android workload.

## Steps

1. **Build** a standalone debug APK (takes minutes; run it in the background):
   `dotnet build BuzzahBuddy/BuzzahBuddy.csproj -f net10.0-android -p:TargetFrameworks=net10.0-android -p:EmbedAssembliesIntoApk=true`
2. **Check the shot list** (below) still fits the app. If a screen changed, edit `scripts/capture.sh`.
3. **Capture:** `bash .claude/skills/android-screenshots/scripts/capture.sh` boots the emulator
   headless if needed and writes `assets/screenshots/Android/YYYY-MM-DD/YYYY-MM-DD-NN-name.png`
   (dark mode; `APPEARANCE=light` for light). It does a clean install, so the first-run notices are
   skipped and nothing from earlier runs shows.
4. **Review every PNG** with Read. Retake (`bash .claude/skills/android-screenshots/scripts/emu.sh shot <name>`)
   anything with a dialog, keyboard, scroll bar, half-cut heading, loading state or Demo Mode banner in view.

## Shot List

Store order, the same five as the `ios-screenshots` skill.

| # | Name | Screen | Why |
|---|------|--------|-----|
| 01 | `session-running` | Session tab, session started | The core job: a therapy session with a clear timer and big Pause/Stop |
| 02 | `gloves` | Gloves tab, both connected | Both gloves, battery and the therapy profiles |
| 03 | `history-overview` | History, scrolled past the banner | Progress: total time, days used, sessions |
| 04 | `history-calendar` | History, calendar in view | Daily use at a glance |
| 05 | `test-motors` | Test Motors, step 3, a few fingers checked | Setup confidence: every finger buzzes |

**Leave out:** Settings, Welcome, forms (Custom Profile), empty or disconnected states, confirm sheets,
and Gloves during a session (it shows a lock banner).

If `BuzzahBuddy/Models/DemoHistory.cs` doesn't exist, History is empty in Demo Mode: skip 03 and 04.

## Driving the Emulator by Hand

`scripts/emu.sh` (run with no arguments for usage): `tap <AutomationId>` (scrolls to it if needed),
`tab <title>`, `scroll <dp>` (from the top), `ids` (what's on screen), `shot <name>`. Positions come
from the screen size, so the script works on other phones too. Leave a pushed page with `adb shell input keyevent BACK`.

## Gotchas

- **The UI dump lists only views on screen,** so `tap` scrolls down until it finds the AutomationId
  (MAUI sets it as the view's `resource-id`).
- **`uiautomator dump` fails while the UI animates** ("null root node"); `emu.sh` retries.
- **Prefs are seeded with `run-as`** into `shared_prefs/com.rbonestell.buzzahbuddy_preferences.xml`
  before first launch. That needs a debug build.
- **`install -g` grants the Bluetooth permissions,** or the nearby-devices prompt covers the scan.
- **Fast swipes fling.** `emu.sh scroll` drags slowly so a position repeats run to run.
- Battery levels, firmware and History are Demo Mode sample data, the same every run.
