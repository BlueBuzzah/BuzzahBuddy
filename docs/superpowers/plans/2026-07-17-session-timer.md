# Session Timer Smoothing + Ring Redesign Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Replace the 5-second-jumping session timer with a locally ticking, device-anchored countdown ring on `GloveControlPage`.

**Architecture:** A pure `SessionClock` model interpolates elapsed time from the device's last `SESSION_STATUS` report; a 500 ms `IDispatcherTimer` in `GloveControlViewModel` ticks it; a new `SessionTimerView` (GraphicsView ring) renders it. Device polls re-anchor the clock (drift ≤3 s absorbed monotonically, larger snaps). Zero extra BLE traffic.

**Tech Stack:** .NET MAUI 9, CommunityToolkit.Mvvm, xUnit. No new dependencies.

**Spec:** `docs/superpowers/specs/2026-07-17-session-timer-design.md`

## Global Constraints

- Existing `.cs`/`.xaml` files use **CRLF** line endings. Modify with the Edit tool only — never rewrite whole files with scripts (a past incident converted a whole file to LF).
- No AI attribution in commits.
- Dark theme only; colors come from the design tokens in `Resources/Styles/Colors.xaml`: `Primary` #35B6F2, `CardBorder` #0A3143, `TextMuted` #a3a3a3, `Warning` #f59e0b, `TextPrimary`.
- Every interactive/visible element needs `SemanticProperties` / `AutomationId` per repo accessibility rules.
- Build check: `dotnet build BuzzahBuddy/BuzzahBuddy.csproj -f net9.0-ios` from the repo root (`net9.0-maccatalyst` is not in TargetFrameworks). Tests: `dotnet test BuzzahBuddy.Tests`.

---

### Task 1: `SessionClock` model (TDD)

**Files:**
- Create: `BuzzahBuddy/Models/SessionClock.cs`
- Test: `BuzzahBuddy.Tests/Models/SessionClockTests.cs`

**Interfaces:**
- Consumes: `SessionStatus` (existing: `Status`, `ElapsedTimeSeconds`, `TotalTimeSeconds`; `SessionState` enum with `RUNNING`, `PAUSED`, `LOW_BATTERY`, `IDLE`).
- Produces (Task 3 relies on these exact signatures):
  - `void SessionClock.Anchor(SessionStatus status, DateTime utcNow)`
  - `SessionClockReading SessionClock.TickTo(DateTime utcNow)`
  - `readonly record struct SessionClockReading(TimeSpan Elapsed, TimeSpan Remaining, double Fraction, bool HasKnownTotal)` with `string ElapsedFormatted` (floor MM:SS) and `string RemainingFormatted` (ceiling MM:SS).

- [ ] **Step 1: Write the failing tests**

`BuzzahBuddy.Tests/Models/SessionClockTests.cs`:

```csharp
using BuzzahBuddy.Models;
using Xunit;

namespace BuzzahBuddy.Tests.Models;

public class SessionClockTests
{
    private static readonly DateTime T0 = new(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc);

    private static SessionStatus Status(SessionState state, int elapsed, int total = 900) =>
        new() { Status = state, ElapsedTimeSeconds = elapsed, TotalTimeSeconds = total };

    [Fact]
    public void TickTo_Running_InterpolatesFromAnchor()
    {
        var clock = new SessionClock();
        clock.Anchor(Status(SessionState.RUNNING, 60), T0);

        var r = clock.TickTo(T0.AddSeconds(2.5));

        Assert.Equal(62.5, r.Elapsed.TotalSeconds, 3);
        Assert.Equal(837.5, r.Remaining.TotalSeconds, 3);
        Assert.Equal(62.5 / 900.0, r.Fraction, 6);
        Assert.True(r.HasKnownTotal);
        Assert.Equal("01:02", r.ElapsedFormatted);   // floor(62.5) = 62s
        Assert.Equal("13:58", r.RemainingFormatted); // ceil(837.5) = 838s
    }

    [Fact]
    public void TickTo_Paused_FreezesAtAnchor()
    {
        var clock = new SessionClock();
        clock.Anchor(Status(SessionState.PAUSED, 60), T0);

        var r = clock.TickTo(T0.AddSeconds(30));

        Assert.Equal(60, r.Elapsed.TotalSeconds, 3);
    }

    [Fact]
    public void Anchor_SmallBackwardDrift_IsAbsorbedMonotonically()
    {
        var clock = new SessionClock();
        clock.Anchor(Status(SessionState.RUNNING, 60), T0);
        // At T0+5 local display shows 65; device reports 63 (2s behind, <= threshold)
        clock.Anchor(Status(SessionState.RUNNING, 63), T0.AddSeconds(5));

        // Display holds at 65 rather than jumping back to 63...
        Assert.Equal(65, clock.TickTo(T0.AddSeconds(5)).Elapsed.TotalSeconds, 3);
        // ...and resumes normal counting once the new anchor catches up.
        Assert.Equal(66, clock.TickTo(T0.AddSeconds(8)).Elapsed.TotalSeconds, 3);
    }

    [Fact]
    public void Anchor_LargeBackwardDrift_Snaps()
    {
        var clock = new SessionClock();
        clock.Anchor(Status(SessionState.RUNNING, 60), T0);
        // Device reports 50 while local shows 65: 15s disagreement > threshold
        clock.Anchor(Status(SessionState.RUNNING, 50), T0.AddSeconds(5));

        Assert.Equal(50, clock.TickTo(T0.AddSeconds(5)).Elapsed.TotalSeconds, 3);
    }

    [Fact]
    public void TickTo_ClampsAtTotal()
    {
        var clock = new SessionClock();
        clock.Anchor(Status(SessionState.RUNNING, 895), T0);

        var r = clock.TickTo(T0.AddSeconds(30));

        Assert.Equal(900, r.Elapsed.TotalSeconds, 3);
        Assert.Equal(0, r.Remaining.TotalSeconds, 3);
        Assert.Equal(1.0, r.Fraction, 6);
        Assert.Equal("00:00", r.RemainingFormatted);
    }

    [Fact]
    public void UnknownTotal_ReportsNoTotalAndInterpolatesElapsed()
    {
        var clock = new SessionClock();
        clock.Anchor(Status(SessionState.RUNNING, 60, total: 0), T0);

        var r = clock.TickTo(T0.AddSeconds(5));

        Assert.False(r.HasKnownTotal);
        Assert.Equal(65, r.Elapsed.TotalSeconds, 3);
        Assert.Equal(0.0, r.Fraction, 6);
    }

    [Fact]
    public void Idle_ReadsZero()
    {
        var clock = new SessionClock();
        clock.Anchor(SessionStatus.CreateIdle(), T0);

        var r = clock.TickTo(T0.AddSeconds(10));

        Assert.Equal(0, r.Elapsed.TotalSeconds, 3);
        Assert.Equal(0.0, r.Fraction, 6);
    }

    [Fact]
    public void LowBattery_CountsAsRunning()
    {
        var clock = new SessionClock();
        clock.Anchor(Status(SessionState.LOW_BATTERY, 60), T0);

        Assert.Equal(62, clock.TickTo(T0.AddSeconds(2)).Elapsed.TotalSeconds, 3);
    }

    [Fact]
    public void PauseThenResume_ContinuesFromResumeAnchor()
    {
        var clock = new SessionClock();
        clock.Anchor(Status(SessionState.RUNNING, 60), T0);
        clock.Anchor(Status(SessionState.PAUSED, 62), T0.AddSeconds(2));
        Assert.Equal(62, clock.TickTo(T0.AddSeconds(30)).Elapsed.TotalSeconds, 3);

        clock.Anchor(Status(SessionState.RUNNING, 62), T0.AddSeconds(30));
        Assert.Equal(63, clock.TickTo(T0.AddSeconds(31)).Elapsed.TotalSeconds, 3);
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test BuzzahBuddy.Tests --filter SessionClockTests 2>&1 | tail -5`
Expected: build FAILURE — `SessionClock` does not exist.

- [ ] **Step 3: Implement `SessionClock`**

`BuzzahBuddy/Models/SessionClock.cs`:

```csharp
namespace BuzzahBuddy.Models;

/// <summary>
/// One reading from a <see cref="SessionClock"/> tick.
/// </summary>
public readonly record struct SessionClockReading(
    TimeSpan Elapsed, TimeSpan Remaining, double Fraction, bool HasKnownTotal)
{
    /// <summary>Elapsed as MM:SS (floored to whole seconds).</summary>
    public string ElapsedFormatted => Format((int)Elapsed.TotalSeconds);

    /// <summary>Remaining as MM:SS (ceiling, so the countdown reaches 00:00 exactly at the end).</summary>
    public string RemainingFormatted => Format((int)Math.Ceiling(Remaining.TotalSeconds));

    private static string Format(int totalSeconds) => $"{totalSeconds / 60:D2}:{totalSeconds % 60:D2}";
}

/// <summary>
/// Local display clock for the therapy session timer. The device is authoritative:
/// every SESSION_STATUS arrival re-anchors the clock via <see cref="Anchor"/>, and
/// <see cref="TickTo"/> interpolates smoothly between anchors so the UI can tick
/// every second without extra BLE traffic. Pure logic — no timers, no MAUI types.
/// </summary>
public class SessionClock
{
    /// <summary>
    /// Anchor disagreements at or below this many seconds are absorbed without a
    /// visible backward jump (the display holds until the device catches up);
    /// larger disagreements snap to device truth.
    /// </summary>
    public const int SnapThresholdSeconds = 3;

    private SessionState _state = SessionState.IDLE;
    private double _anchorElapsed;
    private int _totalSeconds;
    private DateTime _anchorUtc;
    private double _minDisplayElapsed; // monotonic guard while running

    /// <summary>False when the device reported TOTAL:0 (unknown session length).</summary>
    public bool HasKnownTotal => _totalSeconds > 0;

    /// <summary>
    /// Re-anchors the clock to a device report. Call on every SessionStatus arrival.
    /// </summary>
    public void Anchor(SessionStatus status, DateTime utcNow)
    {
        if (IsRunningState(_state) && IsRunningState(status.Status))
        {
            var currentDisplay = Math.Max(ComputeRawElapsed(utcNow), _minDisplayElapsed);
            var backwardDrift = currentDisplay - status.ElapsedTimeSeconds;
            _minDisplayElapsed = backwardDrift > 0 && backwardDrift <= SnapThresholdSeconds
                ? currentDisplay // absorb: hold the display, don't jump back
                : 0;             // in agreement, catching up forward, or snapping back
        }
        else
        {
            _minDisplayElapsed = 0; // state transitions always take device truth
        }

        _state = status.Status;
        _anchorElapsed = status.ElapsedTimeSeconds;
        _totalSeconds = status.TotalTimeSeconds;
        _anchorUtc = utcNow;
    }

    /// <summary>Computes the values the UI should display at <paramref name="utcNow"/>.</summary>
    public SessionClockReading TickTo(DateTime utcNow)
    {
        var elapsed = Math.Max(ComputeRawElapsed(utcNow), _minDisplayElapsed);
        if (elapsed < 0)
            elapsed = 0;
        if (HasKnownTotal && elapsed > _totalSeconds)
            elapsed = _totalSeconds;

        var remaining = HasKnownTotal ? _totalSeconds - elapsed : 0;
        var fraction = HasKnownTotal ? elapsed / _totalSeconds : 0;
        return new SessionClockReading(
            TimeSpan.FromSeconds(elapsed), TimeSpan.FromSeconds(remaining), fraction, HasKnownTotal);
    }

    private double ComputeRawElapsed(DateTime utcNow) =>
        IsRunningState(_state)
            ? _anchorElapsed + (utcNow - _anchorUtc).TotalSeconds
            : _anchorElapsed; // PAUSED freezes; IDLE anchors at 0 via CreateIdle

    private static bool IsRunningState(SessionState state) =>
        state is SessionState.RUNNING or SessionState.LOW_BATTERY;
}
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test BuzzahBuddy.Tests --filter SessionClockTests 2>&1 | tail -3`
Expected: `Passed! - Failed: 0, Passed: 9`

- [ ] **Step 5: Run the full suite (no regressions)**

Run: `dotnet test BuzzahBuddy.Tests 2>&1 | tail -3`
Expected: all pass (90 existing + 9 new).

- [ ] **Step 6: Commit**

```bash
git add BuzzahBuddy/Models/SessionClock.cs BuzzahBuddy.Tests/Models/SessionClockTests.cs
git commit -m "feat: add SessionClock for smooth device-anchored session timekeeping"
```

---

### Task 2: `SessionTimerView` ring control

**Files:**
- Create: `BuzzahBuddy/Views/Controls/SessionTimerView.xaml`
- Create: `BuzzahBuddy/Views/Controls/SessionTimerView.xaml.cs`

**Interfaces:**
- Produces (Task 3 binds these bindable properties): `Progress` (double 0–1), `TimeText` (string), `Caption` (string), `IsPaused` (bool).
- Look at `BuzzahBuddy/Views/Controls/ConnectionStatusBanner.xaml(.cs)` first for this repo's ContentView + BindableProperty conventions and follow them.

- [ ] **Step 1: Create the XAML**

`BuzzahBuddy/Views/Controls/SessionTimerView.xaml`:

```xml
<?xml version="1.0" encoding="utf-8" ?>
<ContentView xmlns="http://schemas.microsoft.com/dotnet/2021/maui"
             xmlns:x="http://schemas.microsoft.com/winfx/2009/xaml"
             x:Class="BuzzahBuddy.Views.Controls.SessionTimerView">
    <!-- Children are excluded from the accessibility tree: the page hosting this
         control exposes one combined semantic description on the control itself. -->
    <Grid>
        <GraphicsView x:Name="RingCanvas"
                      HeightRequest="260"
                      WidthRequest="260"
                      HorizontalOptions="Center"
                      VerticalOptions="Center"
                      AutomationProperties.IsInAccessibleTree="False" />
        <VerticalStackLayout HorizontalOptions="Center"
                             VerticalOptions="Center"
                             Spacing="2"
                             InputTransparent="True"
                             AutomationProperties.IsInAccessibleTree="False">
            <Label x:Name="TimeLabel"
                   FontSize="52"
                   FontAttributes="Bold"
                   TextColor="{StaticResource TextPrimary}"
                   HorizontalTextAlignment="Center" />
            <Label x:Name="CaptionLabel"
                   FontSize="14"
                   TextColor="{StaticResource TextMuted}"
                   HorizontalTextAlignment="Center" />
        </VerticalStackLayout>
    </Grid>
</ContentView>
```

- [ ] **Step 2: Create the code-behind + drawable**

`BuzzahBuddy/Views/Controls/SessionTimerView.xaml.cs`:

```csharp
namespace BuzzahBuddy.Views.Controls;

/// <summary>
/// Circular countdown ring for the therapy session timer: progress arc around a
/// track, with the time and a caption centered inside. Purely presentational —
/// all values are pushed in via bindable properties by GloveControlViewModel.
/// </summary>
public partial class SessionTimerView : ContentView
{
    public static readonly BindableProperty ProgressProperty = BindableProperty.Create(
        nameof(Progress), typeof(double), typeof(SessionTimerView), 0.0, propertyChanged: OnVisualChanged);

    public static readonly BindableProperty TimeTextProperty = BindableProperty.Create(
        nameof(TimeText), typeof(string), typeof(SessionTimerView), string.Empty, propertyChanged: OnVisualChanged);

    public static readonly BindableProperty CaptionProperty = BindableProperty.Create(
        nameof(Caption), typeof(string), typeof(SessionTimerView), string.Empty, propertyChanged: OnVisualChanged);

    public static readonly BindableProperty IsPausedProperty = BindableProperty.Create(
        nameof(IsPaused), typeof(bool), typeof(SessionTimerView), false, propertyChanged: OnVisualChanged);

    // Design tokens (docs/design, Resources/Styles/Colors.xaml) — dark theme only.
    private static readonly Color TextMutedColor = Color.FromArgb("#a3a3a3");   // TextMuted
    private static readonly Color WarningColor = Color.FromArgb("#f59e0b");     // Warning

    private readonly SessionRingDrawable _drawable = new();

    public SessionTimerView()
    {
        InitializeComponent();
        RingCanvas.Drawable = _drawable;
    }

    /// <summary>Session progress fraction, 0.0–1.0.</summary>
    public double Progress
    {
        get => (double)GetValue(ProgressProperty);
        set => SetValue(ProgressProperty, value);
    }

    /// <summary>Large centered time, e.g. "12:47".</summary>
    public string TimeText
    {
        get => (string)GetValue(TimeTextProperty);
        set => SetValue(TimeTextProperty, value);
    }

    /// <summary>Small caption under the time: "remaining", "elapsed", or "Paused".</summary>
    public string Caption
    {
        get => (string)GetValue(CaptionProperty);
        set => SetValue(CaptionProperty, value);
    }

    /// <summary>Dims the arc and shows the caption in the warning color.</summary>
    public bool IsPaused
    {
        get => (bool)GetValue(IsPausedProperty);
        set => SetValue(IsPausedProperty, value);
    }

    private static void OnVisualChanged(BindableObject bindable, object oldValue, object newValue)
    {
        var view = (SessionTimerView)bindable;
        view._drawable.Progress = view.Progress;
        view._drawable.IsPaused = view.IsPaused;
        view.TimeLabel.Text = view.TimeText;
        view.CaptionLabel.Text = view.Caption;
        view.CaptionLabel.TextColor = view.IsPaused ? WarningColor : TextMutedColor;
        view.RingCanvas.Invalidate();
    }
}

/// <summary>
/// Draws the timer ring: a full-circle track with a progress arc from 12 o'clock,
/// clockwise, rounded caps.
/// </summary>
internal class SessionRingDrawable : IDrawable
{
    public double Progress { get; set; }
    public bool IsPaused { get; set; }

    // Design tokens (Resources/Styles/Colors.xaml) — dark theme only.
    private static readonly Color TrackColor = Color.FromArgb("#0A3143"); // CardBorder
    private static readonly Color ArcColor = Color.FromArgb("#35B6F2");   // Primary

    public void Draw(ICanvas canvas, RectF dirtyRect)
    {
        const float stroke = 14f;
        var size = MathF.Min(dirtyRect.Width, dirtyRect.Height) - stroke;
        if (size <= 0)
            return;
        var x = dirtyRect.Center.X - size / 2f;
        var y = dirtyRect.Center.Y - size / 2f;

        canvas.StrokeSize = stroke;
        canvas.StrokeLineCap = LineCap.Round;
        canvas.StrokeColor = TrackColor;
        canvas.DrawEllipse(x, y, size, size);

        var progress = Math.Clamp(Progress, 0.0, 1.0);
        if (progress <= 0)
            return;

        canvas.StrokeColor = IsPaused ? ArcColor.WithAlpha(0.45f) : ArcColor;
        if (progress >= 0.999)
        {
            // A 360° arc degenerates (start == end angle); draw the full circle.
            canvas.DrawEllipse(x, y, size, size);
        }
        else
        {
            // MAUI arc angles are counterclockwise from 3 o'clock; start at 12
            // o'clock (90°) and sweep clockwise by progress * 360°.
            canvas.DrawArc(x, y, size, size, 90f, 90f - (float)(progress * 360.0), clockwise: true, closed: false);
        }
    }
}
```

- [ ] **Step 3: Build**

Run: `dotnet build BuzzahBuddy/BuzzahBuddy.csproj -f net9.0-ios 2>&1 | grep -E " error |Build succeeded|Build FAILED"`
Expected: `Build succeeded.`

- [ ] **Step 4: Commit**

```bash
git add BuzzahBuddy/Views/Controls/SessionTimerView.xaml BuzzahBuddy/Views/Controls/SessionTimerView.xaml.cs
git commit -m "feat: add SessionTimerView countdown ring control"
```

---

### Task 3: Wire clock + ring into the ViewModel and page

**Files:**
- Modify: `BuzzahBuddy/ViewModels/GloveControlViewModel.cs`
- Modify: `BuzzahBuddy/Views/GloveControlPage.xaml` (session status card, ~lines 169–201)

**Interfaces:**
- Consumes: `SessionClock.Anchor(SessionStatus, DateTime)` / `TickTo(DateTime)` / `SessionClockReading` (Task 1); `SessionTimerView.Progress/TimeText/Caption/IsPaused` (Task 2).
- Produces: observable properties `TimerRemainingText`, `TimerElapsedText`, `TimerCaption`, `TimerProgressFraction`, `TimerSemanticDescription` on `GloveControlViewModel`.

- [ ] **Step 1: Add clock fields and observable display properties to the ViewModel**

In `GloveControlViewModel.cs`, next to the existing `_statusPollTimer` field (~line 25), add:

```csharp
    private readonly SessionClock _sessionClock = new();
    private IDispatcherTimer? _displayTickTimer;
```

Next to the existing `[ObservableProperty] private SessionStatus _sessionStatus ...` (~line 58), add:

```csharp
    [ObservableProperty]
    private string _timerRemainingText = "00:00";

    // Nullable on purpose: the page's IsNotNullConverter visibility binding hides
    // the label when there is no known total (null), which "" would not.
    [ObservableProperty]
    private string? _timerElapsedText;

    [ObservableProperty]
    private string _timerCaption = "remaining";

    [ObservableProperty]
    private double _timerProgressFraction;

    [ObservableProperty]
    private string _timerSemanticDescription = string.Empty;
```

- [ ] **Step 2: Add the tick driver and display update methods**

Add these methods next to `StartStatusPolling()`/`StopStatusPolling()` (~line 538):

```csharp
    /// <summary>
    /// Starts the local 500ms display tick that animates the session timer between
    /// 5s BLE status polls. Purely local — no BLE traffic.
    /// </summary>
    private void StartDisplayTick()
    {
        if (_displayTickTimer != null)
            return;
        var timer = Application.Current?.Dispatcher.CreateTimer();
        if (timer == null)
            return;
        timer.Interval = TimeSpan.FromMilliseconds(500);
        timer.Tick += (s, e) => UpdateTimerDisplay(updateSemantics: false);
        timer.Start();
        _displayTickTimer = timer;
    }

    private void StopDisplayTick()
    {
        _displayTickTimer?.Stop();
        _displayTickTimer = null;
    }

    private void UpdateTimerDisplay(bool updateSemantics)
    {
        var reading = _sessionClock.TickTo(DateTime.UtcNow);
        TimerProgressFraction = reading.HasKnownTotal ? reading.Fraction : 0;
        TimerRemainingText = reading.HasKnownTotal ? reading.RemainingFormatted : reading.ElapsedFormatted;
        TimerCaption = IsSessionPaused ? "Paused" : (reading.HasKnownTotal ? "remaining" : "elapsed");
        TimerElapsedText = reading.HasKnownTotal
            ? $"{reading.ElapsedFormatted} elapsed • {SessionStatus.TotalTimeFormatted} total"
            : null;

        // Screen-reader description updates only on re-anchor (~5s), not every tick,
        // to avoid VoiceOver/TalkBack spam.
        if (updateSemantics)
        {
            var state = IsSessionPaused ? "session paused" : "session running";
            if (reading.HasKnownTotal)
            {
                var remaining = TimeSpan.FromSeconds(Math.Ceiling(reading.Remaining.TotalSeconds));
                TimerSemanticDescription =
                    $"{(int)remaining.TotalMinutes} minutes {remaining.Seconds} seconds remaining, {state}";
            }
            else
            {
                TimerSemanticDescription =
                    $"{(int)reading.Elapsed.TotalMinutes} minutes {reading.Elapsed.Seconds} seconds elapsed, {state}";
            }
        }
    }
```

- [ ] **Step 3: Anchor the clock on every status change**

`UpdateSessionState()` (~line 514) is called after every `SessionStatus` assignment (poll success, pushed state change, disconnect teardown). At the **top** of `UpdateSessionState()`, before the existing `IsSessionActive = ...` line, add:

```csharp
        _sessionClock.Anchor(SessionStatus, DateTime.UtcNow);
```

At the **bottom** of `UpdateSessionState()` (after the button-text if/else chain), add:

```csharp
        UpdateTimerDisplay(updateSemantics: true);

        if (IsSessionActive && !_isBackgrounded)
            StartDisplayTick();
        else
            StopDisplayTick();
    }
```

(Replacing the method's original closing brace.)

- [ ] **Step 4: Lifecycle integration**

In `OnAppStopped(...)` (~line 694), after `StopConnectionHealthCheck();` add:

```csharp
        StopDisplayTick();
```

`OnAppResumed` needs no change: it calls `UpdateSessionStatusAsync()`, which reaches `UpdateSessionState()` and restarts the tick with a fresh anchor (the wall-clock anchor makes the first post-resume reading correct even before that poll returns).

Find the cleanup/unsubscribe method near the end of the file (the one whose doc comment says "Unsubscribes from Bluetooth service events and stops timers", ~line 889). Alongside its existing `StopStatusPolling();` call, add:

```csharp
        StopDisplayTick();
```

- [ ] **Step 5: Replace the page's progress bar + labels with the ring**

In `BuzzahBuddy/Views/GloveControlPage.xaml`, first check the root `<ContentPage>` element declares `xmlns:controls="clr-namespace:BuzzahBuddy.Views.Controls"`; add it if missing.

Then inside the session status card, replace this block (~lines 188–199):

```xml
                        <ProgressBar Progress="{Binding SessionStatus.ProgressFraction}"
                                     HeightRequest="12"
                                     ProgressColor="{StaticResource Primary}"
                                     SemanticProperties.Description="Therapy session progress"
                                     AutomationId="SessionProgressBar" />

                        <HorizontalStackLayout Spacing="20" HorizontalOptions="Center">
                            <Label Text="{Binding SessionStatus.ElapsedTimeFormatted, StringFormat='Elapsed: {0}'}"
                                   FontSize="14" />
                            <Label Text="{Binding SessionStatus.RemainingTimeFormatted, StringFormat='Remaining: {0}'}"
                                   FontSize="14" />
                        </HorizontalStackLayout>
```

with:

```xml
                        <controls:SessionTimerView Progress="{Binding TimerProgressFraction}"
                                                   TimeText="{Binding TimerRemainingText}"
                                                   Caption="{Binding TimerCaption}"
                                                   IsPaused="{Binding IsSessionPaused}"
                                                   HeightRequest="270"
                                                   SemanticProperties.Description="{Binding TimerSemanticDescription}"
                                                   AutomationId="SessionTimerRing" />

                        <Label Text="{Binding TimerElapsedText}"
                               FontSize="14"
                               TextColor="{StaticResource TextMuted}"
                               HorizontalTextAlignment="Center"
                               IsVisible="{Binding TimerElapsedText, Converter={StaticResource IsNotNullConverter}}"
                               AutomationProperties.IsInAccessibleTree="False" />
```

Leave the `Status:` row above it unchanged.

- [ ] **Step 6: Build + full test suite**

Run: `dotnet build BuzzahBuddy/BuzzahBuddy.csproj -f net9.0-ios 2>&1 | grep -E " error |Build succeeded|Build FAILED"`
Expected: `Build succeeded.`

Run: `dotnet test BuzzahBuddy.Tests 2>&1 | tail -3`
Expected: all pass.

- [ ] **Step 7: Commit**

```bash
git add BuzzahBuddy/ViewModels/GloveControlViewModel.cs BuzzahBuddy/Views/GloveControlPage.xaml
git commit -m "feat: smooth session timer with countdown ring on control page"
```

---

## On-device verification (manual, after implementation)

With the mock service or real gloves: start a session → ring fills smoothly, countdown ticks every second; pause → digits freeze, caption "Paused" in amber, arc dims; resume → continues; let a 5 s poll land → no visible backward jump; background + resume the app → timer immediately correct; complete a session → ring full, 00:00.
