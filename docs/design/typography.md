# Typography

## Font family

**System fonts only** — the brand rule from bluebuzzah.com
(`system-ui, -apple-system, BlinkMacSystemFont, 'Segoe UI', Roboto, sans-serif`).

In MAUI this means **omitting `FontFamily`** so the platform default applies:
SF Pro (iOS/macOS), Roboto (Android), Segoe UI (Windows). No font assets are bundled.

## Scale

Translated from the website's Tailwind scale to mobile-appropriate pt sizes. Matches the named
label styles in `Styles.xaml`.

| Style key | Size (pt) | Weight | Color | Use |
|---|---|---|---|---|
| `Headline` | 32 | Bold (700) | `#fafafa` | Screen hero titles |
| `Heading1` | 24 | Bold (700) | `#fafafa` | Page titles |
| `Heading2` | 20 | Semibold (600) | `#fafafa` | Section headers, card titles |
| `Heading3` | 17 | Semibold (600) | `#fafafa` | Sub-sections, list group headers |
| `SubHeadline` | 18 | Regular (400) | `#a3a3a3` | Supporting line under a headline |
| `BodyText` | 16 | Regular (400) | `#fafafa` | Default body copy |
| `SecondaryText` | 14 | Regular (400) | `#a3a3a3` | Descriptions, metadata |
| `CaptionText` | 12 | Regular (400) | `#a3a3a3` | Badges, fine print, timestamps |

## Weights

| Weight | Use |
|---|---|
| Regular 400 | Body copy |
| Medium 500 | Emphasis within body, nav labels |
| Semibold 600 | Buttons, subheadings, card titles |
| Bold 700 | Headlines, page titles |

MAUI exposes Regular and Bold via `FontAttributes`; where Medium/Semibold matter (buttons,
headings), size and color contrast carry the hierarchy — do not fake weights with bundled fonts.

## Rules

- Line height: comfortable defaults — roughly 1.2 for headings, 1.5 for body
  (`LineHeight="1.5"` on multi-line body labels).
- No letter-spacing adjustments (website uses none).
- Respect OS font-size accessibility settings: never disable font scaling on text the user reads;
  fixed sizes are acceptable only inside strict fixed-size chrome (e.g. badges).
- Minimum text size 12pt; muted `#a3a3a3` text below 16pt must sit on `#0a0a0a` or `#05212D`
  (see the contrast matrix in [colors.md](colors.md#contrast-matrix)).

## Current implementation gaps

- `MauiProgram.cs` registers **OpenSans** Regular/Semibold and `Styles.xaml` applies
  `FontFamily="OpenSansRegular"` to most controls. Per this spec, `FontFamily` should be removed
  (system default) and the font assets dropped.
