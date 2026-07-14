# BuzzahBuddy - UI/UX Design Guide

<!-- cSpell:ignore vibrotactile affordances WCAG Backgrounded RSSI -->

## Overview

This guide establishes UI/UX principles and patterns for BuzzahBuddy, a therapeutic mobile companion app for BlueBuzzah vibrotactile gloves designed for Parkinson's disease treatment. The guide prioritizes **accessibility-first design** for users with motor impairments, tremors, and varying levels of dexterity.

**Target Audience:** Developers, designers, and AI coding assistants (like Claude Code) working on BuzzahBuddy.

**Visual language:** This guide owns UX and interaction patterns. Colors, typography, per-control styling, and motion are owned by the [design system](docs/design/README.md) (`docs/design/`) — where the two disagree on visual specifics, the design system wins.

---

## Core Design Principles

### 1. **Accessibility Above All**
Every design decision must prioritize users with Parkinson's disease and motor impairments. If a design pattern conflicts with accessibility, choose accessibility every time.

### 2. **Confidence Through Clarity**
Users managing therapeutic devices need absolute confidence in their actions. Provide clear feedback, obvious affordances, and confirmations for critical actions.

### 3. **Forgiveness and Safety**
Tremors cause accidental taps. Design interfaces that prevent errors and make them easy to undo. No destructive actions without confirmation.

### 4. **Consistency Breeds Familiarity**

Use consistent patterns throughout the app. Once a user learns how to interact with one part of the app, those patterns should apply everywhere.

### 5. **Progressive Complexity (Progressive Disclosure)**

Present the simplest path to core functionality first. Advanced features should be available but not overwhelming. Guide users through multi-step processes by providing the right amount of information at each step to make clear choices.

**Key considerations:**

- Sequence features and flows to prevent overwhelming users
- Provide orientation cues so users know where they are and how many steps remain
- Never present all options at once when they can be sequenced logically

### 6. **Purposeful Minimalism**

Every element on screen must earn its place. Display only information that serves the user's current task. Redundant data creates visual clutter, increases cognitive load, and makes screens harder to navigate with assistive technologies. If information doesn't help the user make a decision or take an action, question whether it belongs on that screen.

### 7. **Visual Hierarchy**

Guide users' attention through intentional use of font size, weight, contrast, and spacing. The most important information and actions should be immediately apparent, with secondary elements appropriately de-emphasized. Users should never have to search for critical controls or status information. Like a well-designed book, every screen should have clear navigational cues that help users understand where they are and what they can do.

---

## User Research and Personas

### Understanding BuzzahBuddy Users

Before designing any feature, consider the specific needs and challenges of our primary user personas:

**Primary Persona: "Active Manager"**

- Age: 55-75
- Parkinson's diagnosis: 2-5 years
- Tech comfort: Moderate (uses smartphone daily)
- Motor symptoms: Mild to moderate tremors, primarily in hands
- Goals: Maintain independence, manage symptoms proactively
- Pain points: Small touch targets, complex interfaces, apps that require precision
- Needs: Large controls, clear feedback, ability to use with one hand

**Secondary Persona: "Caregiver Assistant"**

- Age: 45-70 (spouse/adult child)
- Tech comfort: Varies widely
- Goals: Help loved one manage therapy, track progress
- Pain points: Complicated setup, unclear status indicators
- Needs: Simple onboarding, clear instructions, remote monitoring (future feature)

### Design Implications

Every design decision should be validated against these personas:

- Would "Active Manager" be able to use this with tremors?
- Would "Caregiver Assistant" understand how to set this up?
- Does this feature address a real user need or is it just "nice to have"?

### Continuous User Testing

- Conduct usability testing with actual Parkinson's patients (minimum 5 users per major release)
- Test with assistive technologies enabled (VoiceOver, TalkBack, large text)
- Gather feedback from caregiver community
- Analyze app usage data to identify friction points
- Iterate based on real-world usage patterns

---

## Accessibility Requirements

### Touch Targets

**Minimum Requirements:**
- **Size:** 48x48 points minimum (iOS/Android standard)
- **Preferred:** 56x56 points or larger for primary actions
- **Spacing:** 12pt minimum between interactive elements (16pt preferred)
- **Shape:** Square or slightly rectangular; avoid narrow/tall buttons

**Rationale:** Users with tremors need generous hit areas and spacing to avoid accidental activation of adjacent controls.

```
✅ GOOD: Large, well-spaced buttons
┌─────────────────────────┐
│   Start Therapy         │  (56pt height)
└─────────────────────────┘

     (16pt vertical space)

┌─────────────────────────┐
│   Stop Therapy          │  (56pt height)
└─────────────────────────┘


❌ BAD: Small, crowded controls
[Start] [Stop] [Pause] [Reset]  (32pt height, 4pt spacing)
```

### Interaction Patterns

**DO:**
- ✅ Single tap actions
- ✅ Large toggle switches (on/off states)
- ✅ Simple swipes for navigation (entire screen width)
- ✅ Multi-second press-and-hold for destructive actions
- ✅ Voice control support where possible

**DON'T:**
- ❌ Hover interactions (unreliable with tremors)
- ❌ Drag-and-drop (difficult motor task)
- ❌ Pinch-to-zoom gestures (use dedicated zoom buttons)
- ❌ Small sliders with precise positioning requirements
- ❌ Double-tap gestures (can be triggered accidentally)
- ❌ Swipe gestures on small UI elements

### Thumb-Friendly Zones (One-Handed Use)

Optimize for one-handed operation by placing controls within natural thumb reach:

**Easy Reach Zone** (Bottom third of screen):

- Thumb naturally rests here
- Place primary actions: Start/Stop session, main toggles
- Highest priority interactive elements

**Comfortable Zone** (Middle third of screen):

- Requires slight thumb extension
- Place secondary controls: Intensity adjusters, pattern selectors
- Medium priority elements

**Difficult Zone** (Top third of screen):

- Difficult to reach with thumb on large phones
- Avoid interactive elements when possible
- Use for status display and read-only information
- Exception: Standard platform back button (users expect it there)

**Visual diagram:**

```text
┌─────────────────────┐
│  Status & Info      │ ← Difficult (display only)
│                     │
├─────────────────────┤
│  Secondary Controls │ ← Comfortable (toggles, adjustments)
│                     │
├─────────────────────┤
│  Primary Actions    │ ← Easy (start/stop, main button)
└─────────────────────┘
```

**Screen size considerations:**

- Small phones (<375pt): Most of screen is reachable
- Large phones (>428pt): Top half becomes difficult to reach
- Design so no critical action requires reaching the top corners

### Visual Design

**Contrast:**
- **Text on background:** 4.5:1 minimum (WCAG AA)
- **Large text (18pt+):** 3:1 minimum
- **Interactive elements:** 3:1 against adjacent colors
- **Focus indicators:** High contrast, 2pt minimum border

**Typography:**
- **Body text:** 16pt minimum (18pt preferred)
- **Button labels:** 18pt minimum
- **Headings:** 24pt+ with clear hierarchy
- **Line height:** 1.5x for body text (improved readability)
- **Font weight:** Medium (500-600) or Bold for interactive elements

**Color:**
- Dark theme only — use the design system palette ([docs/design/colors.md](docs/design/colors.md)); there is no light mode
- Never rely on color alone to convey information (use icons, text labels, patterns)
- Test all colors at high contrast settings (users may enable OS accessibility features)
- Avoid red/green as sole differentiators (color blindness)

**Animation:**
- Respect `prefers-reduced-motion` system setting
- Use animations sparingly (2-3 second max duration)
- Avoid parallax, bouncing, or rapid transitions
- Provide instant feedback without animation for critical actions

**Proximity and Grouping:**
- Group related UI elements close together (users perceive proximity as relationship)
- Separate unrelated controls with generous spacing (16pt minimum)
- Use visual containers (borders, backgrounds) to reinforce groupings
- Never mix control types within a group (e.g., don't put navigation buttons next to destructive actions)

**Alignment:**
- Use a consistent grid system across all screens (8pt or 12pt baseline grid recommended)
- Align elements consistently (left-align text, center-align buttons, etc.)
- Maintain consistent margins and padding throughout the app
- Avoid arbitrary positioning—every element should align to the grid

### Screen Reader Support

Every interactive element MUST have:
- **Semantic label:** What is this element?
- **Description:** What does it do?
- **Hint:** How do I use it?
- **State:** Is it enabled, selected, loading?

**Example semantic properties:**
```
Button: "Start Therapy"
├─ Description: "Begins a new vibration therapy session with connected gloves"
├─ Hint: "Double tap to activate"
├─ State: "Disabled - No gloves connected"
└─ AutomationId: "StartTherapyButton"

Toggle: "Left Glove"
├─ Description: "Enable or disable vibration for left hand"
├─ Hint: "Swipe up or down to toggle"
├─ State: "On"
└─ AutomationId: "LeftGloveToggle"
```

**Navigation:**
- Logical reading order (top to bottom, left to right)
- Group related controls with headings
- Use semantic heading levels (H1, H2, H3)
- Provide skip navigation for repetitive content

---

## Performance and Responsiveness

Users with Parkinson's disease may experience additional frustration with slow or unresponsive interfaces. Every interaction must feel immediate and predictable.

### Loading and Response Times

- **Target**: All UI interactions respond within 100ms (perceived as instant)
- **Acceptable**: Network operations complete within 2 seconds
- **Required**: Show loading indicators for any operation >500ms

### Performance Optimization

- Compress images and media files (use appropriate formats: WebP for photos, SVG for icons)
- Minimize resource usage during active therapy sessions
- Lazy-load content that isn't immediately visible
- Optimize animations (use GPU-accelerated transforms)
- Test on older/slower devices representative of target users

### Perceived Performance

- Show skeleton screens while content loads (better than spinners)
- Provide immediate visual feedback for all touch interactions
- Use optimistic UI updates (show result before server confirms, rollback if needed)
- Preload next likely screens (e.g., control interface when connecting to glove)

---

## UI Patterns for Parkinson's Users

### Stateful Button Pattern (Critical)

**RULE: Never display multiple buttons for mutually exclusive states simultaneously.**

Users with tremors may accidentally tap the wrong button when presented with multiple options for different states. The UI should always show only the button relevant to the current state.

#### The Anti-Pattern (DON'T DO THIS)

```text
❌ BAD: Both buttons visible simultaneously

┌──────────────┐  ┌──────────────┐
│   Connect    │  │  Disconnect  │
└──────────────┘  └──────────────┘

┌──────────────┐  ┌──────────────┐  ┌──────────────┐
│    Start     │  │    Pause     │  │     Stop     │
└──────────────┘  └──────────────┘  └──────────────┘
```

**Why this is bad:**
- User might accidentally tap "Disconnect" when they meant to scroll
- Multiple buttons for different states creates confusion
- Wastes valuable screen space
- Increases cognitive load (user must determine which button applies to current state)
- Screen readers announce multiple conflicting actions

#### The Correct Pattern (DO THIS)

```text
✅ GOOD: Single button reflects current state

When NOT connected:
┌─────────────────────────┐
│   Connect to Glove      │
└─────────────────────────┘

When connected:
┌─────────────────────────┐
│   Disconnect Glove      │
└─────────────────────────┘

When session NOT active:
┌─────────────────────────┐
│   Start Session         │
└─────────────────────────┘

When session is active:
┌─────────────────────────┐
│   Stop Session          │
└─────────────────────────┘

When session is paused:
┌─────────────────────────┐
│   Resume Session        │
└─────────────────────────┘
```

#### Implementation Guidelines

**State-to-Action Mapping:**

Use conditional rendering to show ONLY the appropriate button for the current state:

| Current State | Show This Button | Action |
|---------------|------------------|---------|
| Disconnected | "Connect" | Initiates connection |
| Connecting | "Connecting..." (disabled) | Show loading indicator |
| Connected (no session) | "Start Session" | Begin therapy session |
| Session active | "Stop Session" | End current session |
| Session paused | "Resume Session" | Continue paused session |
| Disconnecting | "Disconnecting..." (disabled) | Show loading indicator |

**Button State Properties:**

Each stateful button should have:

1. **Clear action verb** - "Connect", "Start", "Stop", "Resume" (not ambiguous labels like "Toggle")
2. **Loading states** - Show "Connecting...", "Starting..." with spinner when processing
3. **Disabled appearance** - When action cannot be performed, show disabled state with explanation
4. **Semantic description** - Screen reader announces both button label AND current state

**Example XAML pattern:**

```xml
<!-- DON'T DO THIS -->
<StackLayout>
  <Button Text="Connect" IsVisible="{Binding IsDisconnected}" />
  <Button Text="Disconnect" IsVisible="{Binding IsConnected}" />
</StackLayout>

<!-- DO THIS INSTEAD -->
<Button Text="{Binding ConnectionButtonText}"
        Command="{Binding ConnectionCommand}"
        IsEnabled="{Binding CanPerformConnectionAction}"
        SemanticProperties.Description="{Binding ConnectionButtonDescription}" />
```

**ViewModel implementation pattern:**

```csharp
// Button text changes based on state
public string ConnectionButtonText => _connectionState switch
{
    ConnectionState.Disconnected => "Connect to Glove",
    ConnectionState.Connecting => "Connecting...",
    ConnectionState.Connected => "Disconnect Glove",
    ConnectionState.Disconnecting => "Disconnecting...",
    _ => "Connect to Glove"
};

// Single command handles state-appropriate action
public ICommand ConnectionCommand => new Command(async () =>
{
    if (_connectionState == ConnectionState.Disconnected)
        await ConnectAsync();
    else if (_connectionState == ConnectionState.Connected)
        await DisconnectAsync();
});
```

#### Exceptions to the Rule

There are LIMITED cases where multiple action buttons are acceptable:

**✅ Acceptable: Buttons perform DIFFERENT, non-conflicting actions**

```text
Session active with two distinct actions:

┌─────────────────────────┐
│   Adjust Settings       │  ← Opens settings (non-destructive)
└─────────────────────────┘

┌─────────────────────────┐
│   Stop Session          │  ← Ends session (destructive)
└─────────────────────────┘
```

**✅ Acceptable: Confirmation dialogs with safe/destructive choice**

```text
┌───────────────┐  ┌───────────────┐
│  Keep Going   │  │  Stop Session │
└───────────────┘  └───────────────┘
(Safe action)      (Destructive action)
```

**❌ NOT Acceptable: Buttons for different states of same action**

```text
❌ WRONG:
┌──────────┐  ┌──────────┐  ┌──────────┐
│  Start   │  │  Pause   │  │   Stop   │
└──────────┘  └──────────┘  └──────────┘

✅ CORRECT:
Show only one button based on current state:
- Not started? → Show "Start Session"
- Running? → Show "Stop Session"
- Paused? → Show "Resume Session"
```

#### Accessibility Benefits

This pattern provides significant accessibility advantages:

1. **Reduces cognitive load** - User only sees relevant action
2. **Prevents errors** - Can't accidentally tap wrong state button
3. **Clearer screen reader experience** - Single action announced, not multiple conflicting options
4. **Simpler navigation** - Fewer focusable elements on screen
5. **Better use of space** - Full button width for large touch target

#### Testing Checklist

- [ ] No page displays multiple buttons for mutually exclusive states
- [ ] Button text clearly indicates action (verb-based: "Connect", "Start", "Stop")
- [ ] Loading states prevent double-taps during transitions
- [ ] Screen reader announces current state AND available action
- [ ] Button remains visible in same location (doesn't jump around on state change)
- [ ] Disabled states include explanatory text ("Connect - No Bluetooth" vs just disabled "Connect")

---

### Information Density and Redundancy

**PRINCIPLE: Show only what the user needs to know for their current task.**

Users with Parkinson's disease already face challenges with motor control. Adding unnecessary cognitive load through redundant or irrelevant information makes the app harder to use. Every piece of text, every icon, every data point should serve a specific purpose.

#### Common Sources of Redundancy to Avoid

**❌ Redundant Status Indicators**

```text
BAD: Multiple ways of showing the same information

Connected Status:
● Blue indicator
"Connected" text label
"Device is connected" description
✓ Checkmark icon
Connection strength bars
```

**✅ Streamlined Status Display**

```text
GOOD: Single, clear status indicator

● BlueBuzzah Left
Connected • 87% Battery
```

**Why this is better:**
- One visual indicator (colored dot) conveys connection state
- Status text provides confirmation for those who can't distinguish colors
- Battery level is actionable information (user may need to charge soon)
- No redundant "connected" badge, checkmark, or description

#### When to Show Information

Ask these questions before displaying any data:

1. **Does this help the user take action?**
   - ✅ Battery at 15% → User should charge device
   - ❌ Device firmware version → User cannot change this

2. **Is this information already conveyed elsewhere?**
   - ✅ "Start Session" button → Clearly indicates no session is active
   - ❌ Separate "Status: Ready to start" label → Redundant with button

3. **Is this relevant to the current task?**
   - ✅ Session timer during active session → User needs to track therapy duration
   - ❌ Total historical session count during active session → Not relevant right now

4. **Would removing this cause confusion?**
   - ✅ "Last connected: 2 hours ago" on pairing screen → Helps user find their device
   - ❌ "App version: 1.2.3" on home screen → Not helpful for typical users

#### Examples of Purposeful Information Display

**Device Connection Screen**

```text
❌ TOO MUCH INFORMATION:

┌────────────────────────────────────────┐
│  Bluetooth Devices                     │
│  Status: Scanning                      │
│  Bluetooth: Enabled                    │
│  Range: 10 meters                      │
│  Protocol: BLE 5.0                     │
│                                        │
│  [🔵] BlueBuzzah Left                  │
│       Device ID: AB:CD:EF:12:34:56     │
│       Signal Strength: -45 dBm         │
│       RSSI: Strong                     │
│       Last Seen: 2 seconds ago         │
│       Battery: 87%                     │
│       Firmware: v2.1.3                 │
│       Status: Available                │
│       Paired: Yes                      │
│                                        │
│  [Connect] ────────────────────────────│
└────────────────────────────────────────┘
```

```text
✅ APPROPRIATE INFORMATION:

┌────────────────────────────────────────┐
│  Available Gloves                      │
│  Searching... ⟳                        │
│                                        │
│  [🔵] BlueBuzzah Left                  │
│       Signal: Strong • 87% Battery     │
│                                        │
│  [Connect] ────────────────────────────│
└────────────────────────────────────────┘
```

**What was removed and why:**
- ❌ MAC address → User can't use this information
- ❌ Bluetooth status/protocol → Not actionable during device selection
- ❌ Signal strength in dBm → Technical metric; "Strong/Weak" is sufficient
- ❌ Firmware version → Only relevant for troubleshooting (can show in device details)
- ❌ "Last seen" timestamp → Scanning is live; device appears if available
- ❌ "Available" status → If it's in the list, it's available
- ❌ "Paired: Yes" → Redundant with "Connect" button vs "Reconnect" button

**Active Session Screen**

```text
❌ INFORMATION OVERLOAD:

┌────────────────────────────────────────┐
│  Session Status: Active                │
│  Started: 2:34 PM                      │
│  Elapsed: 12:34                        │
│  Estimated End: 2:46 PM                │
│  Today's Session #3                    │
│  Total Sessions: 47                    │
│                                        │
│  Left Glove: Connected                 │
│  Battery: 87%                          │
│  Signal: -42 dBm (Excellent)           │
│  Temperature: Normal                   │
│  Vibration: Active                     │
│                                        │
│  Right Glove: Connected                │
│  Battery: 92%                          │
│  Signal: -38 dBm (Excellent)           │
│  Temperature: Normal                   │
│  Vibration: Off                        │
│                                        │
│  Pattern: Steady (Default)             │
│  Intensity: 3/5 (Medium)               │
│  Duration Target: 15 minutes           │
│  Calories Burned: 2.3 kcal             │
└────────────────────────────────────────┘
```

```text
✅ FOCUSED INFORMATION:

┌────────────────────────────────────────┐
│  Active Session • 12:34 elapsed        │
├────────────────────────────────────────┤
│                                        │
│  ●●● Left Glove    [======●  ] ON     │
│  87% Battery                           │
│                                        │
│  ●●● Right Glove   [  ●======] OFF    │
│  92% Battery                           │
│                                        │
│  Intensity: 3/5                        │
│  ┌───┐ ┌───┐ ┌───┐ ┌───┐ ┌───┐       │
│  │ 1 │ │ 2 │ │ 3 │ │ 4 │ │ 5 │       │
│  └───┘ └───┘ └───┘ └───┘ └───┘       │
│                                        │
├────────────────────────────────────────┤
│  [Stop Session]                        │
└────────────────────────────────────────┘
```

**What was removed and why:**
- ❌ "Session Status: Active" → Header already says "Active Session"
- ❌ Start time & estimated end → Elapsed time is what matters during session
- ❌ Session counters → Not relevant during active therapy
- ❌ Signal strength in dBm → Only show if there's a problem
- ❌ Temperature readings → Only alert if abnormal
- ❌ "Vibration: Active/Off" → Toggle switch already shows this
- ❌ Pattern name in separate section → If we add pattern selection, it's interactive, not informational
- ❌ Duration target → Timer shows elapsed; user knows when to stop
- ❌ Calories → This is therapy, not fitness tracking

#### Guidelines for Information Prioritization

**Critical Information (Always Show)**
- Connection status when it affects functionality
- Battery level when it's actionable (<20% or during active session)
- Session timer during active therapy
- Error states and actionable alerts
- Current settings that the user can adjust

**Contextual Information (Show When Relevant)**
- Device signal strength → Only if weak/unstable
- "Last connected" timestamp → Only on pairing screen
- Session history → Only on history screen, not during active session
- Device temperature → Only if overheating warning

**Background Information (Hide or Bury in Settings)**
- Firmware versions
- MAC addresses
- Technical specifications
- Debug information
- Total lifetime statistics (unless user explicitly wants to see them)

#### Screen Reader Considerations

Redundant information is especially problematic for screen reader users:

**❌ Redundant Announcements**
```text
Button with blue checkmark icon labeled "Connected"
Screen reader announces:
"Connected button, connected status, connection confirmed, blue checkmark"
```

**✅ Clear, Concise Announcement**
```text
Button labeled "Disconnect"
Screen reader announces:
"Disconnect button, glove is currently connected"
```

The semantic description provides context (currently connected) while the button label states the available action (disconnect).

#### Testing for Redundancy

Before releasing a screen, ask:

- [ ] Can I remove any text without losing clarity?
- [ ] Are there multiple indicators showing the same state?
- [ ] Is any information displayed but not actionable?
- [ ] Would a new user understand this screen with 50% less text?
- [ ] Does the screen reader announce the same information multiple ways?
- [ ] Are technical details exposed that the user can't act upon?

**Remember:** More information ≠ Better user experience. Clarity through simplicity is especially important for users managing motor impairments.

---

### Confirmation Dialogs

Use confirmation dialogs for:
- Disconnecting from gloves during active session
- Deleting saved therapy sessions
- Resetting app settings
- Exiting mid-session

**Design pattern:**
```
┌─────────────────────────────────────────┐
│  ⚠️  Stop Active Session?               │
│                                         │
│  This will stop vibration on both      │
│  gloves. Your session data will be     │
│  saved.                                 │
│                                         │
│  ┌───────────────┐  ┌───────────────┐  │
│  │  Keep Going   │  │  Stop Session │  │
│  └───────────────┘  └───────────────┘  │
│  (56pt height)      (56pt height)      │
│  (Primary)          (Destructive)      │
└─────────────────────────────────────────┘
```

**Key features:**
- Large action buttons (56pt minimum)
- Safe option on left (or top on small screens)
- Destructive option clearly differentiated (color + icon)
- Clear explanation of consequences
- 16pt spacing between buttons

### Toggle Controls

Prefer large, obvious toggle switches over checkboxes or radio buttons.

**Design:**
- **Size:** 60pt wide × 36pt tall minimum
- **On state:** Primary blue (`#35B6F2`)
- **Off state:** Neutral dark surface (`#0d3a4d`)
- **Tap target:** Entire switch area (not just the thumb)
- **Feedback:** Immediate visual transition + haptic feedback (if available)

**Usage:**
```
Left Glove          [======●  ]  ON
Right Glove         [  ●======]  OFF
```

### Intensity Controls

Avoid small sliders. Use stepped controls with large tap targets.

**Recommended pattern:**
```
Vibration Intensity

┌─────┐  ┌─────┐  ┌─────┐  ┌─────┐  ┌─────┐
│  1  │  │  2  │  │  3  │  │  4  │  │  5  │
└─────┘  └─────┘  └─────┘  └─────┘  └─────┘
 Low                           High

(Each button: 56×56pt, 12pt spacing)
```

**Alternative for advanced users:**
- If a slider is necessary, make it **at least 300pt wide**
- Use 10-12 discrete steps (not continuous)
- Show current value numerically above slider
- Allow tapping anywhere on the track to jump to that value
- Provide -/+ buttons as alternative input method

### Status Indicators

Use large, obvious visual indicators for connection state, battery level, and session progress.

**Connection Status:**
```
┌──────────────────────────────────┐
│  ●  BlueBuzzah Left              │  ← 24pt icon
│     Connected • 87% Battery      │  ← 18pt status text
└──────────────────────────────────┘
```

**Color coding:**
- 🔵 Blue (`#35B6F2`): Connected, battery >50% — blue is the brand's success/active color, never green
- 🟡 Amber (`#f59e0b`): Connected, battery 20-50%
- 🔴 Danger (`#fb7185` text / `#e11d48` fill): Disconnected or battery <20%
- ⚪ Muted (`#a3a3a3`): Not paired

**Battery Level:**
- Show percentage numerically (don't rely on icon alone)
- Use both color and icon state
- Alert user at 20% and 10% battery

---

## Page Layouts and Navigation

### Navigation Patterns (Use Standard Conventions)

**DO use familiar patterns:**

- ✅ Bottom tab bar for main sections (iOS and Android standard)
- ✅ Back button in top-left (platform convention)
- ✅ Settings in tab bar or top-right menu
- ✅ Swipe gestures for full-screen navigation (back/forward between pages)

**DON'T create custom patterns:**

- ❌ Custom gesture controls that conflict with system gestures
- ❌ Hidden navigation (hamburger menus that hide critical features)
- ❌ Unconventional icon placements (e.g., back button on right)
- ❌ Novel interaction patterns that require learning

**Principle**: Users have learned navigation patterns from other apps. Leverage this existing mental model rather than forcing them to learn something new.

### Bottom Navigation (Recommended)

Use bottom tab navigation for main app sections. Bottom placement is easier to reach for users with limited range of motion.

**Tab Bar:**
- **3-4 tabs maximum** (avoid overflow)
- **Icon + Label** on each tab (don't use icon-only)
- **56pt minimum height** for entire tab bar
- **Active state:** High contrast color + underline/indicator

**Suggested tabs:**
1. **Home/Dashboard** - Overview, quick start
2. **Control** - Active glove control during session
3. **History** - Past sessions, statistics
4. (Optional) **Settings** - App configuration, help

### Page Structure

**Consistent layout across all pages:**
```
┌────────────────────────────────────┐
│  [Back]           Page Title       │  ← 56pt header
├────────────────────────────────────┤
│                                    │
│  Primary Content Area              │
│  (Scrollable if needed)            │
│                                    │
│                                    │
│                                    │
│                                    │
├────────────────────────────────────┤
│  [Primary Action Button]           │  ← 72pt footer
└────────────────────────────────────┘
│  [Tab Bar Navigation]              │  ← 56pt tab bar
└────────────────────────────────────┘
```

**Key principles:**
- Primary action button always visible (no scrolling required)
- Critical controls never hidden under hamburger menus
- Generous padding: 16-24pt margins around content
- Single column layout on phones (no multi-column text)

### Empty States

When no data exists (e.g., no gloves paired, no session history), provide clear guidance.

**Empty state template:**
```
┌────────────────────────────────────┐
│                                    │
│         [Large Icon]               │  ← 64×64pt icon
│                                    │
│    No Gloves Connected             │  ← 24pt heading
│                                    │
│  Connect your BlueBuzzah gloves    │  ← 16pt body text
│  to start a therapy session.       │
│                                    │
│  ┌──────────────────────────────┐  │
│  │    Scan for Gloves           │  │  ← 56pt button
│  └──────────────────────────────┘  │
│                                    │
└────────────────────────────────────┘
```

---

## Bluetooth & Glove Interaction UX

### Device Discovery and Pairing Flow

**Step 1: Scan for Devices**
- Clear "Scan" button (56pt height)
- Visual indication of scanning state (spinner + "Scanning..." text)
- Timeout after 30 seconds with option to retry
- List discovered devices in real-time as they're found

**Device List Item:**
```
┌────────────────────────────────────────┐
│  [🔵]  BlueBuzzah Left                 │  ← 72pt height
│        Signal: Strong • Not paired     │  ← 16pt status
│                                        │
│  [Connect] ────────────────────────────│  ← 48pt button
└────────────────────────────────────────┘
```

**Step 2: Connection Process**
- Show loading state on "Connect" button (spinner + "Connecting...")
- Display connection progress if possible (e.g., "Verifying device...")
- Success feedback: Haptic + visual confirmation + sound (if enabled)
- Transition to device detail or control page automatically

**Step 3: Connection Failure**
- Clear error message explaining what went wrong
- Actionable next steps ("Make sure glove is powered on")
- Retry button or return to device list

**Error Message Example:**
```
┌───────────────────────────────────────┐
│  ⚠️  Connection Failed                │
│                                       │
│  Could not connect to BlueBuzzah      │
│  Left. Please check:                  │
│                                       │
│  • Glove is powered on                │
│  • Glove is within 10 feet            │
│  • Bluetooth is enabled               │
│                                       │
│  ┌─────────────┐  ┌─────────────┐    │
│  │  Try Again  │  │  Cancel     │    │
│  └─────────────┘  └─────────────┘    │
└───────────────────────────────────────┘
```

### Real-Time Glove Control Interface

When gloves are connected and ready, present a simple, focused control interface.

**Primary Control Page:**
```
┌────────────────────────────────────────┐
│  Active Session • 12:34 elapsed        │  ← Header
├────────────────────────────────────────┤
│                                        │
│  ●●● Left Glove    [======●  ] ON     │  ← Status + toggle
│  87% Battery                           │
│                                        │
│  ●●● Right Glove   [  ●======] OFF    │
│  92% Battery                           │
│                                        │
│  ─────────────────────────────────────│
│                                        │
│  Vibration Pattern                     │
│  ┌────────────┐ ┌────────────┐        │
│  │  Steady    │ │  Pulsing   │        │  ← Pattern selector
│  │  [●●●●●]   │ │  [● ● ● ]  │        │
│  └────────────┘ └────────────┘        │
│                                        │
│  Intensity: 3/5                        │
│  ┌───┐ ┌───┐ ┌───┐ ┌───┐ ┌───┐       │
│  │ 1 │ │ 2 │ │ 3 │ │ 4 │ │ 5 │       │  ← Step selector
│  └───┘ └───┘ └───┘ └───┘ └───┘       │
│                                        │
├────────────────────────────────────────┤
│  ┌──────────────────────────────────┐ │
│  │     Stop Session                 │ │  ← Primary action
│  └──────────────────────────────────┘ │
└────────────────────────────────────────┘
```

**Key design elements:**
- Session timer always visible at top
- Connection status for each glove with visual indicator (color + icon)
- Independent controls for left/right gloves
- Large, clear pattern and intensity selectors
- Single, obvious "Stop Session" button
- No hidden menus or secondary screens during active session

### Session Management

**Starting a Session:**
1. Ensure gloves are connected (check both left and right if applicable)
2. Show pre-session settings (pattern, intensity, duration - optional timer)
3. Large "Start Session" button
4. Provide visual and haptic feedback when session begins
5. Transition to active control interface

**During Session:**
- Show elapsed time prominently
- Allow adjustments to pattern and intensity without stopping
- Provide "Pause" option if needed (with clear visual indication of paused state)
- Monitor battery and warn if low (<20%)
- Monitor connection and alert if glove disconnects

**Ending a Session:**
- "Stop Session" button always visible
- Confirm if session duration < 2 minutes (possible accidental stop)
- Save session data automatically
- Show session summary (duration, patterns used, avg intensity)
- Offer options: "Start New Session" or "View History"

**Session Summary Example:**
```
┌────────────────────────────────────────┐
│  ✓ Session Complete                    │
│                                        │
│  Duration: 15 minutes 32 seconds       │
│  Gloves: Both (Left + Right)           │
│  Pattern: Steady vibration             │
│  Average Intensity: 3/5                │
│                                        │
│  ┌──────────────────────────────────┐ │
│  │    Start New Session             │ │
│  └──────────────────────────────────┘ │
│                                        │
│  ┌──────────────────────────────────┐ │
│  │    View Session History          │ │
│  └──────────────────────────────────┘ │
└────────────────────────────────────────┘
```

### Status and Feedback Patterns

**Real-Time Feedback:**
When user changes a setting (pattern, intensity, toggle glove on/off), provide immediate feedback:
1. **Visual:** Button state change, color shift, icon update
2. **Haptic:** Short vibration (if device supports and user hasn't disabled)
3. **Screen reader:** Announce change ("Intensity set to 4", "Left glove turned on")
4. **No delay:** Changes apply instantly (no "Save" button required)

**Micro-Interactions and Animations:**
Small, purposeful animations enhance perceived responsiveness:

*Button Interactions:*

- Scale slightly on press (0.95x transform, 100ms duration)
- Change color/elevation to indicate active state
- Return to normal state on release
- Provide haptic feedback (if available and user hasn't disabled)

*Transitions:*

- Use consistent transition durations (200-300ms for most UI changes)
- Ease-in-out curves for natural motion
- Avoid bouncing or elastic effects (can trigger nausea/discomfort)
- Respect `prefers-reduced-motion` setting (disable or minimize animations)

*Examples of effective feedback:*

- Button press: Visual depression + haptic pulse
- Toggle switch: Smooth slide animation + color change
- Success action: Checkmark animation + brief blue flash
- Connection established: Pulse animation on connection indicator
- Error state: Gentle shake animation (2-3 cycles, 300ms total)

**Connection Status Banner:**
Display a persistent banner when connection is lost:
```
┌────────────────────────────────────────┐
│  ⚠️ Left Glove Disconnected            │  ← 56pt banner
│     Attempting to reconnect...         │
│     [Dismiss] [Reconnect Now]          │
└────────────────────────────────────────┘
```

**Battery Warnings:**
- **20% remaining:** Subtle notification (banner or toast)
- **10% remaining:** More prominent warning
- **5% remaining:** Alert recommending user stops session and charges device

**Loading States:**
Never leave users wondering if something is happening:
- Show spinners for operations >0.5 seconds
- Provide progress indicators for multi-step operations (e.g., "Connecting... Step 2 of 3")
- Use skeleton screens for content that's loading (e.g., session history list)
- Disable buttons during processing (with loading indicator on button)

---

## Color and Theme Guidelines

The full palette, WCAG contrast matrix, and per-control color specs live in the
[design system](docs/design/colors.md). This section covers only how to apply it.

### Semantic Color Usage

Use colors consistently to convey meaning (BlueBuzzah brand rule: **blue is success — never green**):

**Primary / Success / Active (Brand):**
- App branding, primary action buttons, selected states, connected devices, successful operations, progress
- `#35B6F2` — always with dark `#0a0a0a` text/icons on blue fills

**Warning:**
- Low battery, weak signal, non-critical alerts, "attention needed" states
- `#f59e0b` (dark text on warning fills)

**Danger/Error:**
- Disconnected devices, errors, destructive actions
- `#e11d48` fills with `#fafafa` text; `#fb7185` for danger text/icons on dark surfaces

**Neutral:**
- Inactive states, disabled controls: muted `#a3a3a3`; surfaces step `#0a0a0a → #05212D → #0d3a4d`

### Dark Theme Only

BuzzahBuddy is dark-theme only (`AppTheme.Dark` is forced) — there is no light mode, matching
the BlueBuzzah brand. Do not add light-mode variants or `AppThemeBinding` pairs.

- Background: `#0a0a0a`; cards `#05212D`
- Text: `#fafafa`; muted `#a3a3a3`
- Borders: `#0A3143`

**Every text/background pair must appear in the approved
[contrast matrix](docs/design/colors.md#contrast-matrix) (WCAG AA minimum).**

### Avoid Over-Reliance on Color

Always pair color with another indicator:
- ✅ Blue checkmark icon + "Connected" text
- ✅ Red warning icon + "Disconnected" text
- ✅ Disabled button: gray background + low opacity + cursor change

---

## Responsive Design and Screen Sizes

### Layout Breakpoints

**Small phones (< 375pt width):**
- Single column layouts only
- Larger buttons (60pt height preferred)
- Reduce padding to 12pt if necessary (but maintain 48pt touch targets)

**Standard phones (375-428pt width):**
- Primary target size (most Parkinson's patients use standard phones)
- 56pt buttons, 16pt padding, 12pt spacing between elements

**Tablets and large phones (> 428pt width):**
- Consider two-column layouts for control interface (left/right glove side-by-side)
- Don't scale buttons beyond 72pt height (diminishing returns)
- Use extra space for larger padding, not smaller touch targets

### Orientation

**Portrait (Recommended):**
- Primary design target
- Easier to hold with one hand or steadily with two hands
- Bottom navigation accessible

**Landscape:**
- Support it, but don't optimize for it
- Maintain minimum touch target sizes
- May need to adjust layouts to prevent UI stretching

---

## Error Handling and Edge Cases

### Graceful Degradation

**Bluetooth Disabled:**
- Detect when Bluetooth is off
- Show clear message explaining why app can't function
- Provide direct link to device settings (if platform allows)

**No Gloves Paired:**
- Empty state with clear call-to-action
- Guide user through pairing process
- Provide link to help documentation or video tutorial

**Glove Disconnects During Session:**
- Pause session automatically
- Show reconnection banner
- Attempt auto-reconnect (3 attempts, 5 seconds apart)
- If reconnection fails, prompt user to manually reconnect or end session

**App Backgrounded During Session:**
- Maintain Bluetooth connection (request background permission)
- Show notification indicating session is still active
- When app returns to foreground, verify glove is still connected

### Error Message Best Practices

**DO:**
- ✅ Use plain language ("Can't connect" not "Connection failed with error -3001")
- ✅ Explain what happened and why
- ✅ Provide actionable next steps
- ✅ Offer a way to retry or get help
- ✅ Use appropriate emotional tone (calm, reassuring, not alarming)

**DON'T:**
- ❌ Show technical error codes without explanation
- ❌ Blame the user ("You entered invalid data")
- ❌ Use jargon ("BLE GATT service unavailable")
- ❌ Leave users with no path forward

---

## Onboarding and First-Time Experience

### Welcome Flow

**First launch sequence:**
1. **Welcome screen** - Brief intro to BuzzahBuddy and its purpose
2. **Permissions request** - Explain why Bluetooth permission is needed
3. **Tutorial (optional)** - Quick tour of main features (can be skipped)
4. **Pair first glove** - Guide user through first device connection

**Key principles:**
- Keep it short (3-4 screens maximum)
- Allow skipping tutorial (provide access to help later)
- Request permissions with context (explain *why* before asking)
- Get user to first value quickly (connect glove within 2 minutes)

### Contextual Help

**Help Button:**
- Available on every page (top-right or in settings)
- Opens contextual help for current screen
- Includes links to FAQs, video tutorials, contact support

**Tooltips:**
- Use sparingly (too many is overwhelming)
- Only for features that aren't self-evident
- Dismissible and don't show again after first dismissal
- Large touch target to dismiss (entire screen or large "Got it" button)

---

## Accessibility Testing Checklist

Before releasing any UI update, verify:

- [ ] All interactive elements are **48×48pt minimum** (56pt preferred)
- [ ] **Spacing** between interactive elements is 12pt minimum
- [ ] All text is **16pt minimum** (18pt preferred for body text)
- [ ] **Color contrast** meets WCAG AA (4.5:1 for text, 3:1 for interactive elements)
- [ ] Every interactive element has **semantic properties** (label, description, hint)
- [ ] **Screen reader** can navigate entire page in logical order
- [ ] App functions with **VoiceOver (iOS)** and **TalkBack (Android)** enabled
- [ ] No **hover-only interactions** (all features accessible via tap)
- [ ] **Focus indicators** are visible with keyboard/switch control navigation
- [ ] Animations **respect prefers-reduced-motion** setting
- [ ] App works in both **light and dark modes**
- [ ] **Error messages** are clear, actionable, and non-technical
- [ ] All critical actions have **confirmation dialogs**
- [ ] **Loading states** are obvious (spinners, progress indicators)
- [ ] **Empty states** provide clear guidance and calls-to-action
- [ ] **No mutually exclusive state buttons** displayed simultaneously (Connect/Disconnect, Start/Stop/Pause)
- [ ] **No redundant information** - each data point serves a unique, actionable purpose
- [ ] **Screen reader** doesn't announce the same information multiple ways

---

## Component Library Reference

### Recommended .NET MAUI Controls

**Buttons:**
- `<Button>` - Standard for primary actions
- Minimum HeightRequest: 56
- FontSize: 18 minimum
- CornerRadius: 8 (slightly rounded, not fully pill-shaped)

**Toggles:**
- `<Switch>` - For binary on/off states
- MinimumHeightRequest: 36, MinimumWidthRequest: 60
- Use `OnColor` property for high contrast

**Text Input:**
- `<Entry>` - Single line text
- `<Editor>` - Multi-line text
- HeightRequest: 48 minimum
- FontSize: 18 minimum
- Avoid when possible (therapy app shouldn't require much text input)

**Lists:**
- `<CollectionView>` - For device lists, session history
- ItemSpacing: 12 minimum
- Minimum item height: 72pt

**Dialogs:**
- `DisplayAlert()` - For confirmations and errors
- Customize button text to be action-oriented ("Stop Session" not "Yes")

**Layout:**
- `<Grid>` - For structured layouts with multiple regions
- `<StackLayout>` - For vertical/horizontal stacking
- Use generous padding: 16-24pt

---

## Testing Strategy and Iteration

### Usability Testing Protocol

**Pre-Release Testing (Required for Major Updates):**

1. **Internal testing**: Development team tests with accessibility features enabled
2. **Expert review**: Accessibility specialist reviews against WCAG 2.1 AA
3. **User testing**: 5-8 participants from target demographic (Parkinson's patients)
4. **Caregiver testing**: 3-5 caregivers test onboarding and setup flows
5. **Assistive technology testing**: Full app navigation with VoiceOver and TalkBack

**Test Scenarios:**

- First-time setup and glove pairing
- Starting and stopping a therapy session
- Adjusting intensity during active session
- Recovering from connection loss
- Navigating with screen reader only
- Using app with large text settings enabled
- One-handed operation throughout entire flow

### A/B Testing for Design Decisions

When choosing between design alternatives, use A/B testing:

- Button sizing: 48pt vs 56pt vs 64pt (measure accidental taps and user preference)
- Intensity control: Stepped buttons vs large slider (measure time to adjust, errors)
- Color schemes: Test contrast ratios and user preference in both light/dark modes

### Analytics and Behavioral Data

**Key metrics to track:**

- Session completion rate (% of started sessions that complete)
- Time to first successful connection
- Frequency of accidental button presses (detected by rapid undo actions)
- Features used vs. features ignored (inform future simplification)
- Error rates by screen (identify confusing interfaces)
- Accessibility feature usage (screen reader, large text, etc.)

### Iteration Cycle

1. **Release** → 2. **Gather data** (2-4 weeks) → 3. **Analyze patterns** → 4. **Identify improvements** → 5. **Test changes** → 6. **Release**

**Continuous improvement priorities:**

- Fix critical usability issues immediately (hotfix release)
- Address accessibility gaps within one release cycle
- Iterate on UX improvements based on user feedback quarterly
- Never remove accessibility features once added (only enhance)

---

## Resources and References

### Accessibility Standards
- [WCAG 2.1 Level AA Guidelines](https://www.w3.org/WAI/WCAG21/quickref/)
- [iOS Human Interface Guidelines - Accessibility](https://developer.apple.com/design/human-interface-guidelines/accessibility)
- [Material Design Accessibility](https://m3.material.io/foundations/accessible-design/overview)

### Parkinson's-Specific Design
- [Designing for Users with Motor Disabilities](https://www.w3.org/WAI/people-use-web/abilities-barriers/#physical)
- [WebAIM: Motor Disabilities](https://webaim.org/articles/motor/)

### .NET MAUI UI/UX
- [.NET MAUI User Interface Documentation](https://learn.microsoft.com/en-us/dotnet/maui/user-interface/)
- [MAUI Community Toolkit](https://learn.microsoft.com/en-us/dotnet/communitytoolkit/maui/)

### Testing Tools
- iOS: **VoiceOver** (Settings > Accessibility > VoiceOver)
- Android: **TalkBack** (Settings > Accessibility > TalkBack)
- iOS: **Accessibility Inspector** (Xcode)
- Android: **Accessibility Scanner** (Google Play Store)

---

## Version History

- **v1.4** (2026-07-14) - Aligned with the BlueBuzzah design system (`docs/design/`):
  - Visual language (colors, typography, controls, motion) now deferred to the design system
  - Dark theme only — removed light-mode guidance
  - Blue `#35B6F2` replaces green for all success/connected/active states
  - Status color coding updated to design-system tokens (amber warning, `#fb7185`/`#e11d48` danger, `#a3a3a3` muted)
- **v1.3** (2025-01-09) - Integrated mobile UX best practices and UI design principles:
  - Added "Visual Hierarchy" as 7th core principle
  - Enhanced "Progressive Complexity" with progressive disclosure guidance
  - Added "Proximity and Grouping" and "Alignment" to visual design guidelines
  - Added comprehensive "Performance and Responsiveness" section
  - Enhanced "Micro-Interactions and Animations" guidance with specific patterns
  - Added "User Research and Personas" section with BuzzahBuddy-specific personas
  - Added "Thumb-Friendly Zones" for one-handed use optimization
  - Added "Testing Strategy and Iteration" section with usability protocols
  - Enhanced navigation section to emphasize standard platform patterns
- **v1.2** (2025-01-09) - Added "Purposeful Minimalism" principle and comprehensive "Information Density and Redundancy" section with practical examples
- **v1.1** (2025-01-09) - Added Stateful Button Pattern section and enhanced accessibility guidelines
- **v1.0** (2025-10-26) - Initial design guide created

---

## Questions or Feedback?

For questions about implementing these design patterns or suggesting improvements to this guide, refer to the BuzzahBuddy project repository and community resources.

**Remember: When in doubt, prioritize accessibility over aesthetics. Users with Parkinson's disease depend on this app to manage their therapy safely and confidently.**
