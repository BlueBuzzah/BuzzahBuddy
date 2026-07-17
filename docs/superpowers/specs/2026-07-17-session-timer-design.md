# Therapy Session Timer — Design

**Date:** 2026-07-17
**Status:** Approved

## Problem

The session timer on `GloveControlPage` only updates when a `SESSION_STATUS` BLE
poll returns — every 5 seconds (`SessionStatusPollIntervalSeconds`), deliberately
throttled to protect <5 ms bilateral glove sync. The `Elapsed:`/`Remaining:`
labels and `ProgressBar` therefore jump in 5-second increments with BLE jitter
on top. The phone never ticks on its own.

## Goals

- Timer updates smoothly (sub-second ring motion, per-second digits) with zero
  additional BLE traffic.
- Device remains authoritative: polls re-anchor the clock; pauses, completion,
  and drift corrections come from the device.
- World-class visual: large countdown ring replacing the debug-style labels.
- Fully covered by unit tests where the logic lives.

## Non-goals

- No change to the BLE polling cadence or firmware.
- No new dependencies (no SkiaSharp/Lottie).
- No redesign of other `GloveControlPage` sections.

## Architecture

### 1. `SessionClock` (`Models/SessionClock.cs`) — pure timekeeping

No timers, no MAUI types; fully unit-testable.

- **Anchor:** `Anchor(SessionStatus status, DateTime utcNow)` stores the
  device's `ELAPSED`/`TOTAL`/state and the arrival instant. Called on every
  `SessionStatus` arrival (poll result or pushed state change).
- **Tick:** `TickTo(DateTime utcNow)` returns displayed elapsed, remaining, and
  progress fraction:
  - RUNNING → `anchorElapsed + (utcNow − anchorTime)`, clamped to `[0, total]`.
  - PAUSED → frozen at anchor.
  - IDLE/complete → reset / clamped at 00:00.
- **Drift correction:** on re-anchor, a disagreement ≤ 3 s is absorbed with a
  monotonic guard (displayed remaining never increases while RUNNING); larger
  disagreements (missed polls, resume from background) snap to device truth.
- **Lifecycle:** anchor is wall-clock (`DateTime.UtcNow`) based, so the first
  tick after app resume is automatically correct — no special resume handling.
- **TOTAL:0 fallback:** remaining/fraction report "unknown"; elapsed still
  interpolates.

### 2. Tick driver — `GloveControlViewModel`

A MAUI `IDispatcherTimer` at 500 ms, started when a session becomes active and
stopped when it ends (alongside the existing status-poll lifecycle, including
the background/resume pause paths). Each tick calls `TickTo` and updates
observable display properties (`TimerRemainingText`, `TimerElapsedText`,
`TimerProgressFraction`, `TimerCaption`). Existing 5 s BLE poll unchanged; its
handler now also calls `SessionClock.Anchor(...)`.

### 3. `SessionTimerView` (`Views/Controls/`) — visual

`GraphicsView` + `IDrawable` circular ring, replacing the current
`Elapsed:/Remaining:` labels and horizontal `ProgressBar` in
`GloveControlPage.xaml`:

- Track: full circle in `CardBorder` (#0A3143); progress arc in `Primary`
  (#35B6F2), rounded caps, from 12 o'clock, filling clockwise as the session
  progresses.
- Center: remaining time `MM:SS` (~52 pt bold, `TextPrimary`) over a small
  "remaining" caption (`TextMuted`). Elapsed/total shown small beneath the ring.
- Paused: arc dims, caption "Paused" in `Warning` (#f59e0b), digits frozen.
- Complete: ring full; existing completion announcement unchanged.
- TOTAL unknown: center shows elapsed counting up; ring shows static track.
- Dark theme only, per `docs/design/` tokens.

### 4. Accessibility

The ring is a single semantic element ("12 minutes 47 seconds remaining,
session running"). Its description updates on re-anchor (~5 s), not every tick,
to avoid screen-reader spam. Start/pause/complete announcements unchanged. No
new touch targets.

## Testing

xUnit tests for `SessionClock`: running interpolation, pause freeze,
resume, re-anchor small-drift absorption + monotonic countdown, large-drift
snap, completion clamp at 00:00, TOTAL:0 fallback, IDLE reset. Visual verified
on simulator/device.

## Files touched

- `BuzzahBuddy/Models/SessionClock.cs` (new)
- `BuzzahBuddy/Views/Controls/SessionTimerView.xaml(.cs)` or drawable class (new)
- `BuzzahBuddy/ViewModels/GloveControlViewModel.cs` (tick driver, anchor calls)
- `BuzzahBuddy/Views/GloveControlPage.xaml` (swap labels/bar for the ring)
- `BuzzahBuddy.Tests/Models/SessionClockTests.cs` (new)
