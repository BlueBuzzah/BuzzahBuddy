---
name: ios-screenshots
description: Use when capturing, refreshing or framing BuzzahBuddy iOS App Store screenshots, when screens changed and the store images need retaking, or when adding the iPhone hardware frame to a folder of screenshots.
---

# iOS App Store Screenshots

Captures BuzzahBuddy on the **iPhone 17 Pro** simulator in Demo Mode (no gloves needed), then frames
each shot in `assets/screenshots/iOS/iphone-17-pro-frame.png`. Run everything from the repo root.

## Needs

Xcode with an iPhone 17 Pro simulator, `idb` and `idb_companion` on PATH, ImageMagick 7 (`magick`),
and the .NET iOS workload.

## Steps

1. **Build** (takes minutes; run it in the background):
   `dotnet build BuzzahBuddy/BuzzahBuddy.csproj -f net10.0-ios -p:TargetFrameworks=net10.0-ios`
2. **Check the shot list** (below) still fits the app. If a screen changed, edit `scripts/capture.sh`.
3. **Capture:** `bash .claude/skills/ios-screenshots/scripts/capture.sh` writes
   `assets/screenshots/iOS/YYYY-MM-DD/YYYY-MM-DD-NN-name.png` (dark mode; `APPEARANCE=light` for light).
   It does a clean install, so the first-run notices are skipped and nothing from earlier runs shows.
4. **Review every PNG** with Read. Retake (`bash .claude/skills/ios-screenshots/scripts/sim.sh shot <name>`) anything with an alert,
   keyboard, scroll bar, half-cut heading, loading state or Demo Mode banner in view.
5. **Frame:** `bash .claude/skills/ios-screenshots/scripts/frame.sh assets/screenshots/iOS/YYYY-MM-DD`
   writes `<folder>/framed/`. It frames every `.png` in the folder (not subfolders) and skips any
   that isn't 1206×2622. Read one framed image to check it.

## Shot List

Store order. Each shot shows something the app does for the person, with real-looking data.

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

## Driving the Simulator by Hand

`scripts/sim.sh` (run with no arguments for usage): `tap <AutomationId>` (scrolls to it if needed),
`tab <title>`, `scroll <points>` (from the top), `ids` (what's on screen), `shot <name>`. Coordinates
are points on a 402×874 screen.

## Gotchas

- **idb says the device is "not booted"** when it is: run `idb kill` and retry.
- **Tab bar items are missing from `ids`;** use `sim.sh tab`. The Custom row's Edit Settings button is
  missing too (its card is one accessible element).
- **Prefs come from launch arguments** (`-demo_mode YES` etc.), not `simctl spawn defaults write`,
  which writes outside the app's container. Toggling Demo Mode in the app won't stick for that run.
- **Fast swipes fling.** `sim.sh scroll` drags slowly so a position repeats run to run.
- Xcode 27 has no Simulator.app; the simulator runs headless and that's fine.
- Battery levels, firmware and History are Demo Mode sample data, the same every run.
- Framed images (1350×2760) are marketing art, not an App Store Connect screenshot size.
