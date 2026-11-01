---
name: ios-screenshots
description: Use when capturing or refreshing BuzzahBuddy iOS App Store screenshots, or when screens changed and the store images need retaking.
---

# iOS App Store Screenshots

Captures BuzzahBuddy in Demo Mode (no gloves needed) on one of three simulators. Always dark mode, for
now. Run everything from the repo root.

| Device | Simulator | Shots |
|--------|-----------|-------|
| `iPhone` (default) | "iPhone 17 Pro Screenshots", created on first run | 1206×2622 portrait |
| `Duo` | "iPhone Duo", unfolded | 2007×2853 portrait, inner display |
| `iPad` | "iPad Pro 13-inch Screenshots", created on first run | 2752×2064 landscape (App Store 13") |

The iPhone and iPad get their own simulators so other agents' runs don't collide with them.

## Needs

Xcode (plus an iPhone Duo simulator for `Duo`), `idb` and `idb_companion` on PATH, and
the .NET iOS workload. The scripts check these and stop with a pointer here:

- **Duo:** unfold it first.
- **iPad, once per simulator:** turn it to landscape with the iOS Simulator control tool (`rotate`;
  simctl can't rotate), and set Settings > Multitasking & Gestures > **Full Screen Apps** (the default,
  Windowed Apps, adds a resize corner and the app's name to the status bar). Both settings stick.
- **iPad build:** the app must declare iPad (`UIDeviceFamily` 2 in `Platforms/iOS/Info.plist`), or it
  runs as a scaled-up iPhone app. Pass a build that does as the `.app` argument.

## Steps

1. **Build** (takes minutes; run it in the background):
   `dotnet build BuzzahBuddy/BuzzahBuddy.csproj -f net10.0-ios -p:TargetFrameworks=net10.0-ios`
2. **Check the shot list** (below) still fits the app. If a screen changed, edit `scripts/capture.sh`.
3. **Capture:** `bash .claude/skills/ios-screenshots/scripts/capture.sh [iPhone|Duo|iPad] [path/to/BuzzahBuddy.app]`
   writes `assets/screenshots/iOS/<device>/YYYY-MM-DD/YYYY-MM-DD-NN-name.png`. It does a clean install,
   so the first-run notices are skipped and nothing from earlier runs shows.
4. **Review every PNG** with Read. Retake (`DEVICE=Duo bash .claude/skills/ios-screenshots/scripts/sim.sh shot <name>`;
   `DEVICE` defaults to iPhone) anything with an alert, keyboard, scroll bar, half-cut heading, loading
   state or Demo Mode banner in view.

## Shot List

Store order, the same five on every device. Each shot shows something the app does for the person,
with real-looking data. The iPad's two-pane screens show more per shot (History pairs the overview or
calendar with the days list).

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

## Driving the Simulator by Hand

`scripts/sim.sh` (run with no arguments for usage): `tap <AutomationId>` (scrolls to it if needed),
`tab <title>`, `scroll <points>` (from the top), `ids` (what's on screen), `shot <name>`. Coordinates
are points: 402×874 on the iPhone, 669×951 on the unfolded Duo, 1376×1032 on the landscape iPad.
History scrolls are per device in `capture.sh` (the iPad's History starts higher).

## Gotchas

- **idb says the device is "not booted"** when it is: run `idb kill` and retry.
- **Tab bar items are missing from `ids`;** use `sim.sh tab`. The Custom row's Edit Settings button is
  missing too (its card is one accessible element).
- **Prefs come from launch arguments** (`-demo_mode YES` etc.), not `simctl spawn defaults write`,
  which writes outside the app's container. Toggling Demo Mode in the app won't stick for that run.
- **Fast swipes fling.** `sim.sh scroll` drags slowly so a position repeats run to run.
- **A tap on a moving page only stops it,** so `tap` waits 2 s after each scroll before tapping.
- **Drag inside the column that scrolls.** `tap` drags at the element's own x; `scroll` drags at `sx`
  (`sim.sh`), which is the left column on the iPad. A drag between columns moves nothing.
- **"◀ Settings" in the status bar** means the app was launched from Settings; `prepare` quits it first.
- **Nothing else may drive the simulator during a run.** Another session or a tap in a simulator
  panel moves the app off the script's path. The iPhone's own simulator avoids that; the Duo is shared.
- **`ids` prints only `- | None`** when the simulator's accessibility is stuck: reboot that simulator.
- **The Duo has two displays.** `shot` passes `--display=primary-1` (inner); idb already works in
  inner-display points.
- Xcode 27 has no Simulator.app; the simulator runs headless and that's fine.
- Battery levels, firmware and History are Demo Mode sample data, the same every run.
