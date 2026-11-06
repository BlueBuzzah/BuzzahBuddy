---
name: guide-screenshot
description: Use when capturing or refreshing BuzzahBuddy user guide images, filling a screenshot placeholder in tmp/user-guide/user-guide.md, or when a screenshot needs an arrow, callout or label pointing at part of the app.
---

# BuzzahBuddy User Guide Screenshots

Takes one guide image at a time from the iOS simulator in Demo Mode, with design-system arrows on it when
the spec asks for them. Run everything from the repo root.

**REQUIRED:** read `.claude/skills/ios-screenshots/SKILL.md` first. Its Needs, build step, `sim.sh` commands,
Gotchas and retake rules all apply here. Skip its Shot List and `capture.sh`, which are for the App Store.

## Steps

1. **Get the spec.** If the request names a guide image, find its placeholder in
   `tmp/user-guide/user-guide.md`: the file name is the shot name, *Shows* is the screen, *Arrow* is the
   arrows. Otherwise take these from the request, and draw an arrow only if it asks for one.
2. **Check it's reachable** in the tables below. If it isn't, say so and stop. Don't fake the state, edit
   the app, or paint it into the image.
3. **Get to the screen.** Run `sim.sh prepare` once per batch (a build must exist), then drive it with
   `sim.sh`. Run `sim.sh ids` to find arrow targets.
4. **Shoot:**
   ```bash
   python3 .claude/skills/guide-screenshot/scripts/guide_shot.py 09-session-ready \
     --arrow SessionControlButton from=above
   ```
   This reads element positions and takes the shot together, so run it while the screen is exactly right.
   It writes `tmp/user-guide/images/<name>.png`, and the raw shot stays in `assets/screenshots/iOS/`. With
   no `--arrow` it's a plain copy.
5. **Review the PNG with Read.** Check the *Shows* line and the ios-screenshots retake rules. Each arrow
   should point clearly at its target and run through empty space: the default side only has the most
   room, it isn't always clear. Nudge `from=`, `len=` and `at=` until the arrow and label cross no words,
   then rerun.
6. **Report** the path, plus one line of alt text naming the screen and what each arrow points to.

## Arrows

| Option | Meaning |
|---|---|
| `TARGET` | AutomationId, exact accessibility label from `sim.sh ids`, `tab:Gloves`, or `x,y,w,h` in points. Join with `+` for a group: `PauseResumeButton+StopSessionButton` |
| `from=` | `above`, `below`, `left`, `right`. Default: the side with the most room |
| `len=` | Length in points, default 80. Vary it to stagger neighbors |
| `at=` | Where it lands along the target's edge, 0 to 1. Use it to clear text the arrow would cross |
| `label=` | Label past the tail, in Title Case for control names. Use only when the spec asks for one |

Use `x,y,w,h` for what has no element of its own. Switches, a check inside a finger button, and links
inside a card (Change on `ChangeProfileCard`) are all like this: an element's frame can be a whole row or
card. To find the points, scale from a known frame in the same image, because Read shows a downsized copy.
After any scroll, measure again.

The style is the design system's "Screenshot callouts" rule (its `README.md`). The script reads colors
from `tokens.json` and holds the sizes as constants, so keep the two in step. Images stay dark: `prepare`
forces dark mode.

## Reaching Each Shot

Start from `sim.sh prepare`. "Connected" means `tap FindDevicesButton`, `tap ConnectDeviceButton`, wait 5 s.

| Guide shot | Route |
|---|---|
| 01, 09 | Connected, `tab Session` |
| 02 | Connected, `tab Session`. Tabs are `tab:Session` and so on; arrows must cross Start Session, so stagger with `len=70` and `len=135` and use `at=` to dodge its text |
| 03 | Relaunch with no notices flag (below) |
| 04 | Straight after `prepare` |
| 05 | `tap FindDevicesButton` |
| 06 | Connected, `tab Gloves`, `tap ProfileRow_4`, wait 3 s. Only the bottom rows fit on screen with `ApplyDeviceSettingsButton` |
| 07 | From 06, `tap ApplyDeviceSettingsButton`, `tap ConfirmSheetConfirm`, wait 1 s, shoot. It lasts a few seconds |
| 08 | From 06, `idb ui tap` Edit Settings, which has no element: it sits about 274 right and 138 down from `ProfileRow_4`'s top-left, and the row moves with scroll, so read its frame first. This form uses plain number fields, not steppers: Buzz Length is `TimeOnEntry` |
| 10 | Connected, `tab Session`, `tap SessionControlButton`, wait 8 s. The ring's target is its `Session running…` label from `ids` |
| 11 | From 10, `tap StopSessionButton` |
| 13 | Connected, then as in `capture.sh`: `tab Gloves`, `tap CalibrateMotorsButton`, `tap NextStepButton` twice, tap some fingers. `tap BackButton` to leave |
| 15 | Connected, `tab History`, `tap PreviousMonthButton`, `scroll 190`. Period is `Segment30+Segment0` |
| 17 | Connected, `tab History`, then `idb ui swipe --udid <udid> 201 700 201 150 --duration 1.5` twice, because `scroll` tops out near 600. Don't use `tap ShareReportButton`: it opens the share sheet |
| 19 | `tab Settings`, `scroll 56` so the Demo Mode card clears the tab bar. The switch has no element: use `x,y,w,h` |

To reach Welcome, relaunch without the flag. Relaunch with it to get past Welcome (see Gotchas):

```bash
udid=$(bash .claude/skills/ios-screenshots/scripts/sim.sh udid); app=com.rbonestell.buzzahbuddy
xcrun simctl terminate $udid $app
xcrun simctl launch $udid $app -demo_mode YES -ThemePreference 0   # add -WelcomeNoticesAccepted YES to skip Welcome
```

## Out of Reach

| Guide shot | Why |
|---|---|
| 12 Add a Note | Demo Mode never saves a session, so the prompt never shows. Needs real gloves |
| 16 Day Note | Demo Mode hides Edit Note. Sample days do show notes, but the spec's arrow target needs real history |
| 14 Firmware banner | Skipped in Demo Mode. Needs real gloves on old firmware |
| 22 Secondary Not Connected | Demo Mode's secondary is always linked. Needs real gloves |
| 18 Report PDF | A rendered file, not an app screen |
| 20 Siri, 21 Shortcuts | Outside the app. Needs a build with a team ID (see the repo's CLAUDE.md) or a real device |

## Gotchas

- **`sim.sh tap` swipes instead of tapping below `h - 114` points** (760 on the iPhone), assuming a tab bar
  covers it. On a screen with no tab bar that does damage: on the Stop sheet it ended the session instead
  of tapping Keep Going. For Continue, Keep Going or other low controls with no tab bar, tap by point:
  `idb ui tap --udid "$udid" <x> <y>`.
- **Off-screen elements report real frames** (negative y, or past the bottom). The script stops on these:
  scroll first. It can't see a target hidden under the tab bar, so check that in step 5.
- **Demo Mode's gloves are v2:** the scan card reads "BlueBuzzah v2", firmware 2.0.0, four fingers.
- **`sim.sh scroll` lands about 10 points short** on small scrolls. A small scroll can also leave a title
  half cut at the top, so check that in step 5.
- **The Ready ring breathes,** so it can look dim. Retake a few seconds later.
- **The Dynamic Island's black pill shows in some shots and not others.** The cause isn't known. Retake to
  keep a set consistent.
- **The simulator can shut down mid-batch.** Run `prepare` again, then redo the steps. If `prepare` right
  after a boot says to rotate the device, just rerun it.
- **Taps stop landing after a reboot** (`dtuhidd connection ... invalidated`), though `ids` still works.
  Run `idb kill`.
- **Applying a profile changes the sample state:** Session then shows that profile. `prepare` resets it.
- **Repeated ids** (`StepperDecrease`) resolve to the first on screen. Use `x,y,w,h` for a later one.
- **The running ring's label changes** as time passes. Read it from `ids` right before shooting.

## Check

`python3 .claude/skills/guide-screenshot/scripts/test_guide_shot.py` covers the arrow and label geometry.
