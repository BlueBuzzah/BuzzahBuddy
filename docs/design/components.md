# Components

Per-control specs with exact colors per state. Every pair here appears in the
[contrast matrix](colors.md#contrast-matrix). All interactive controls: **44pt minimum
touch target**, visible `#35B6F2` focus indicator, 200ms state transitions.

## Layout constants

| Token | Value | Website source |
|---|---|---|
| Radius — buttons, inputs, chips | 8 | `rounded-lg` / `--radius: 0.5rem` |
| Radius — cards | 12 | `rounded-xl` |
| Radius — badges/pills | full (height/2) | `rounded-full` |
| Card padding | 24 (16 acceptable on narrow phones) | `p-6` |
| Page gutter | 24 | `px-6` |
| Spacing scale | 4-pt increments: 4 / 8 / 12 / 16 / 24 / 32 / 48 | Tailwind scale |

## Buttons

All buttons: Semibold, radius 8, padding 24h/12v, 44pt min height.

### PrimaryButton — the one blue CTA per screen

| State | Background | Text | Extra |
|---|---|---|---|
| Default | `#35B6F2` | `#0a0a0a` | Glow `#35B6F2` @ 30%, radius 20 |
| Pressed | `#2da8e0` | `#0a0a0a` | Scale 0.98 |
| Disabled | `#0d3a4d` | `#a3a3a3` | No glow |

**Never light text on the blue fill** — dark text is the brand's signature button look (8.6:1).

### SecondaryButton — outline

| State | Background | Border | Text |
|---|---|---|---|
| Default | Transparent | `#0A3143` | `#fafafa` |
| Pressed | `#05212D` | `#35B6F2` @ 30% | `#fafafa` |
| Disabled | Transparent | `#0A3143` @ 50% | `#a3a3a3` @ 50% |

### TertiaryButton — text/ghost

Transparent, no border, `#35B6F2` text; pressed shows `#35B6F2` @ 10% fill. On `#0d3a4d`
surfaces this is AA (5.3:1) — keep tertiary labels ≥ 14pt Semibold there.

### DangerButton — destructive actions only

`#dc2626` fill, `#fafafa` text (4.6:1 AA), no glow. Pressed: darken to `#b91c1c`.
Always behind a confirmation per `DESIGN_GUIDE.md`.

### Nav pill (website "Downloads" pattern)

`#35B6F2` @ 10% fill, `#35B6F2` @ 20% border, `#fafafa` Medium text.

## Cards

The core surface. MAUI pattern: `Border` + `RoundRectangle` corner radius 12.

| Property | Value |
|---|---|
| Background | Gradient `#05212D → #082735` (135°), or flat `#05212D` |
| Border | 1pt `#0A3143` |
| Padding | 24 |
| Title | `Heading2`, `#fafafa` |
| Body | `SecondaryText`, `#a3a3a3` |
| Emphasis card | Add glow `#35B6F2` @ 20–25%, radius 15–30 (`CardWithGlow`) |
| Nested/elevated element | `#0d3a4d` fill |

Depth = background steps `#0a0a0a → #05212D → #0d3a4d`. Never gray/black drop shadows.

## Entry / Editor / SearchBar *(invented — website has no form inputs; derived from card + border + ring tokens)*

| State | Background | Border | Text | Placeholder |
|---|---|---|---|---|
| Default | `#05212D` | 1pt `#0A3143` | `#fafafa` | `#a3a3a3` |
| Focused | `#05212D` | 1pt `#35B6F2` | `#fafafa` | `#a3a3a3` |
| Error | `#05212D` | 1pt `#fb7185` | `#fafafa` | — + `#fb7185` 12pt helper text below |
| Disabled | `#0a0a0a` | 1pt `#0A3143` @ 50% | `#a3a3a3` | `#a3a3a3` @ 50% |

Radius 8, min height 44. MAUI `Entry` has no native border — wrap in a `Border` styled as above.

## Badges / chips

Pill (radius = height/2), 12pt Medium text, padding 8h/4v. Variants per the
[chip table](colors.md#translucent-chip-variants): blue fill @ 10–20% with `#35B6F2` text;
warning `#f59e0b`; danger text `#fb7185`. Attention state ("Coming Soon", "Update available")
may pulse — see [motion.md](motion.md).

## Status indicators (connection, battery, therapy state)

| State | Color | Example |
|---|---|---|
| Good / active / connected | `#35B6F2` | Connected dot, therapy running, progress |
| Warning | `#f59e0b` | Low battery, weak signal |
| Problem | `#fb7185` text / `#e11d48` fill | Disconnected, error banner |
| Inactive / unknown | `#a3a3a3` | Not paired, battery unavailable |

Never color alone: pair every status color with a label or icon (color-blind users).

## Selection & toggle controls

| Control | On | Off | Thumb/track detail |
|---|---|---|---|
| `Switch` | Track `#35B6F2`, thumb `#0a0a0a` | Track `#0d3a4d`, thumb `#a3a3a3` | |
| `Slider` | Active track `#35B6F2` | Inactive `#0d3a4d` | Thumb `#35B6F2` |
| `ProgressBar` | Fill `#35B6F2` | Track `#0d3a4d` | Blue = progress, never green |
| `ActivityIndicator` | `#35B6F2` | — | |
| `CheckBox` / `RadioButton` | `#35B6F2` | Border `#0A3143` | Check glyph `#0a0a0a` on blue |
| `Picker` / `DatePicker` | Text `#fafafa` on `#05212D` | — | Styled like Entry |

## Icons

Stroke-based (2pt stroke, no fill), matching the website's SVG style. Color follows the text
color of their context: `#fafafa` default, `#35B6F2` accent, `#a3a3a3` muted. Icon chips:
32/40/48pt square, radius 8 (12 at 48pt), `#35B6F2` @ 10% fill, `#35B6F2` icon.

## Overlays & dialogs

Scrim `#000000` @ 50% (`OverlayBackground`). Dialog surface: card spec (flat `#05212D`,
border `#0A3143`, radius 12). Destructive confirm uses DangerButton + SecondaryButton pair.

## Current implementation gaps (`Styles.xaml`)

- Named styles (`PrimaryButton`, `SecondaryButton`, `TertiaryButton`, heading/body styles,
  glow variants) already exist and views use them consistently — audit their values against
  this spec rather than restyling views.
- `Entry`/`Editor` implicit styles exist but lack the `Border` wrapper pattern (no border,
  no focus ring, no error state).
- Status colors in styles inherit the teal-success gap from `Colors.xaml` (see colors.md).
