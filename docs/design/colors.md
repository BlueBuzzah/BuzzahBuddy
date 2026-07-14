# Colors

All values extracted from `BlueBuzzah.com/index.html` (Tailwind config + `:root` CSS custom
properties) unless marked *(invented — no website precedent)*.

## Core tokens

| Token | MAUI resource | Hex | Website source | Usage |
|---|---|---|---|---|
| Background | `PageBackground` | `#0a0a0a` | `background` | Page background |
| Foreground | `TextPrimary` | `#fafafa` | `foreground` | Primary text, headings, icons on dark |
| Primary | `Primary` | `#35B6F2` | `primary` | Buttons, links, focus, active states, success, info, glows |
| Primary foreground | `PrimaryDarkText` | `#0a0a0a` | `primary-foreground` | Text/icons **on** primary blue fills |
| Card | `CardBackground` | `#05212D` | `secondary` / `--card` | Cards, elevated surfaces |
| Card light | `CardBackgroundLight` | `#0d3a4d` | `secondary-light` | Second elevation step, pressed card states |
| Card gradient end | — | `#082735` | `hsl(199,74%,12%)` | End stop of card gradient |
| Border | `CardBorder` | `#0A3143` | `hsl(199,74%,15%)` | All borders and dividers |
| Muted | `TextMuted` | `#a3a3a3` | `muted` / `--muted-foreground` | Secondary text, captions, metadata |

## Semantic status colors

The website defines no status colors (its only precedent is a `yellow-500` disclaimer chip).
These follow the brand rule **blue = success, never green**; warning and danger are invented for
distinguishability in a therapy app.

| State | Fill / accent | Text-on-dark | Notes |
|---|---|---|---|
| Success | `#35B6F2` (Primary) | `#35B6F2` | Brand rule. Connected, complete, therapy active |
| Info | `#35B6F2` (Primary) | `#35B6F2` | Merged into Primary — no separate token |
| Warning | `#f59e0b` | `#f59e0b` | Low battery, weak signal. Dark `#0a0a0a` text on warning fills |
| Danger | `#e11d48` (fills, with `#fafafa` text) | `#fb7185` | *(invented)* — see contrast note below |

**Danger contrast rule:** `#e11d48` as *text* on `#0a0a0a` is 4.2:1 — fails AA for body text.
Use `#fb7185` for danger text/icons on dark surfaces; reserve `#e11d48` (or `#dc2626` for
buttons) as a *fill* with `#fafafa` text (4.5:1 / 4.6:1 AA).

### Translucent chip variants

The website's badge/chip pattern uses the accent color at low alpha over the dark base:

| Variant | Fill | Border | Content |
|---|---|---|---|
| Primary chip / badge | `#35B6F2` @ 10% (`#1A35B6F2`) | none, or `#35B6F2` @ 30% for emphasis | `#35B6F2` |
| Primary chip (strong) | `#35B6F2` @ 20% (`#3335B6F2`) | `#35B6F2` @ 30% | `#35B6F2` |
| Warning chip | `#f59e0b` @ 10% | none | `#f59e0b` |
| Danger chip | `#e11d48` @ 10% | none | `#fb7185` |

## Contrast matrix

Approved text/background pairs (WCAG 2.1 ratios, computed):

| Text | Background | Ratio | Grade | Use |
|---|---|---|---|---|
| `#fafafa` | `#0a0a0a` | 19.0:1 | AAA | Body text on page |
| `#fafafa` | `#05212D` | 15.9:1 | AAA | Text on cards |
| `#fafafa` | `#0d3a4d` | 11.6:1 | AAA | Text on light cards |
| `#0a0a0a` | `#35B6F2` | 8.6:1 | AAA | **Primary button text** |
| `#35B6F2` | `#0a0a0a` | 8.6:1 | AAA | Links, accents on page |
| `#35B6F2` | `#05212D` | 7.2:1 | AAA | Links, accents on cards |
| `#35B6F2` | `#0d3a4d` | 5.3:1 | AA | Accents on light cards |
| `#a3a3a3` | `#0a0a0a` | 7.8:1 | AAA | Muted text on page |
| `#a3a3a3` | `#05212D` | 6.6:1 | AA | Muted text on cards |
| `#a3a3a3` | `#0d3a4d` | 4.8:1 | AA | Muted text on light cards (body size and up only) |
| `#f59e0b` | `#0a0a0a` / `#05212D` | 9.2:1 / 7.7:1 | AAA | Warning text |
| `#0a0a0a` | `#f59e0b` | 9.2:1 | AAA | Text on warning fills |
| `#fb7185` | `#0a0a0a` / `#05212D` | 7.4:1 / 6.2:1 | AAA / AA | Danger text |
| `#fafafa` | `#e11d48` / `#dc2626` | 4.5:1 / 4.6:1 | AA | Text on danger fills |

### Forbidden pairs

| Text | Background | Ratio | Why it happens |
|---|---|---|---|
| `#fafafa` (or any light) | `#35B6F2` | 2.0:1 | Habit from other design systems — blue buttons here take **dark** text |
| `#e11d48` | `#0a0a0a` / `#05212D` | 4.2:1 / 3.5:1 | Use `#fb7185` for danger text instead |
| `#a3a3a3` | `#35B6F2` or any status fill | ≤ 2:1 | Muted text belongs on dark surfaces only |

## Gradients

```
Card gradient:  linear 135°, #05212D → #082735
Hero/ambient:   radial #35B6F2 @ 15% alpha over #0a0a0a (decorative only)
```

MAUI: `LinearGradientBrush` `StartPoint 0,0` `EndPoint 1,1`, stops `#05212D` (0.0) → `#082735` (1.0).
A flat `#05212D` is an acceptable substitute where gradients are impractical.

## Glow recipes

All glows are the primary blue at varying alpha — never gray/black shadows.

| Effect | Value |
|---|---|
| Resting glow (cards, primary buttons) | `#35B6F2` @ 20–30% alpha, radius ≈ 15–20 |
| Hover/emphasis glow | `#35B6F2` @ 50% alpha, radius ≈ 30 |
| MAUI `Shadow` | `Brush=#35B6F2, Opacity=0.3, Radius=20, Offset=0,0` |

## Current implementation gaps (`Colors.xaml`)

All gaps above were remediated on 2026-07-14 (see docs/superpowers/plans/2026-07-14-design-system-remediation.md).
