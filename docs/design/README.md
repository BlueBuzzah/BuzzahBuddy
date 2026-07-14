# BuzzahBuddy Design System

The authoritative visual specification for the BuzzahBuddy .NET MAUI app, derived from the
[bluebuzzah.com](https://www.bluebuzzah.com) website source (`BlueBuzzah.com/index.html`), which is the
canonical expression of the BlueBuzzah brand.

## Scope and relationship to other docs

| Document | Owns |
|---|---|
| **This design system** (`docs/design/`) | Visual language: color tokens, typography, per-control styling, motion |
| `DESIGN_GUIDE.md` (repo root) | UX and interaction patterns: touch targets, stateful buttons, confirmation flows, navigation, personas |
| `Resources/Styles/Colors.xaml`, `Styles.xaml` | Implementation. Where the XAML deviates from this spec, the spec wins — deviations are flagged in each doc under **Current implementation gaps** and are remediated in separate work, not silently |

## Documents

- [colors.md](colors.md) — color tokens, semantic status colors, WCAG contrast matrix, gradients and glow recipes
- [typography.md](typography.md) — font family, size scale, weights
- [components.md](components.md) — per-control specs with exact colors per state
- [motion.md](motion.md) — transitions, press feedback, entrance animations, reduced motion

## Core principles

1. **Dark theme only.** There is no light mode. The app forces `AppTheme.Dark`.
2. **Blue is success.** `#35B6F2` indicates success, progress, active, and info states. Never green.
3. **System fonts.** Platform-native type (SF Pro on iOS, Roboto on Android). No bundled fonts.
4. **Depth from color, not shadow.** Hierarchy comes from layered navy tones and subtle blue glows —
   never heavy drop shadows.
5. **Contrast is non-negotiable.** Every text/background pair must appear in the
   [approved contrast matrix](colors.md#contrast-matrix). Primary buttons use **dark text on blue**
   (8.6:1), never light text on blue (2.0:1 — fails).
6. **Accessibility floor:** 44pt minimum touch targets, WCAG AA minimum for all text, visible blue
   focus indicators, reduced-motion support.
