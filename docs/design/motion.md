# Motion

From the website's animation system. Motion is subtle, quick, and always optional.

## Timing

| Token | Value | Use |
|---|---|---|
| Standard transition | 200ms, ease-in-out | All state changes (color, opacity, glow) |
| Entrance | 400–600ms, ease-out | Content appearing |
| Stagger step | 100ms | Sequential list/section entrances (max 6 steps) |

## Patterns

| Pattern | Spec | Website source |
|---|---|---|
| Press feedback | Scale to 0.98 on press, spring back on release (~100ms each way) | `btn-primary:active` |
| Emphasis on touch | Glow alpha 30% → 50% while pressed | `btn-primary:hover` |
| Fade-in-up | Opacity 0→1 + translateY 20→0, 600ms ease-out | `fadeInUp` |
| Fade-in-down | Opacity 0→1 + translateY −10→0, 400ms ease-out | `fadeInDown` (nav/banner) |
| Pulse glow | Opacity oscillates 0.4 ↔ 0.7, 3s ease-in-out loop | `pulseGlow` — attention badges only |

MAUI: `FadeTo`/`TranslateTo`/`ScaleTo` with `Easing.CubicOut`; stagger via per-item delay.

## Rules

- Motion communicates state; nothing animates purely for decoration except entrance fades.
- One pulsing element per screen, maximum.
- **Reduced motion is mandatory:** when the OS reports reduce-motion, collapse all animations to
  instant (or ≤ 10ms) — content appears in place, press feedback becomes a color change only,
  pulse effects render static at 0.7 opacity.
- Never block interaction on an animation completing.
