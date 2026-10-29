using BuzzahBuddy.Helpers;
using BuzzahBuddy.Messages;
using BuzzahBuddy.Models;
using CommunityToolkit.Mvvm.Messaging;

namespace BuzzahBuddy.Views.Controls;

/// <summary>
/// Lucide's radar drawn from its own paths so only the sweep line turns, once every 2.5
/// seconds while running, and each blip lights as the sweep passes it. Under reduced motion
/// it is the still Lucide icon. Decorative: the text beside it says what's happening.
/// <para>
/// The sweep runs for exactly as long as <see cref="IsRunning"/>, never gated on Loaded:
/// iOS detects Loaded for this view only through a frame change, so when the scan list's
/// empty view is detached and re-attached at the same size, Loaded never fires again
/// and a Loaded-gated sweep stayed still on every scan after the first.
/// </para>
/// </summary>
public sealed class RadarSweep : GraphicsView
{
    private const string SweepAnimation = "RadarSweep";
    private const uint TurnLength = 2500;

    public static readonly BindableProperty IsRunningProperty = BindableProperty.Create(
        nameof(IsRunning), typeof(bool), typeof(RadarSweep), false, propertyChanged: (b, _, _) => ((RadarSweep)b).Apply());

    private readonly RadarDrawable _drawable = new();

    public RadarSweep()
    {
        Drawable = _drawable;
        AutomationProperties.SetIsInAccessibleTree(this, false);
        // Weak: needs no unregistering.
        WeakReferenceMessenger.Default.Register<RadarSweep, ThemeChangedMessage>(this, (radar, _) => radar.Invalidate());
        // The animation ticker comes from the handler; a radar created mid-scan starts here.
        HandlerChanged += (_, _) => Apply();
    }

    public bool IsRunning { get => (bool)GetValue(IsRunningProperty); set => SetValue(IsRunningProperty, value); }

    private void Apply()
    {
        // A scan always ends (timeout or Stop), so the sweep can't outlive it.
        if (IsRunning && Handler is not null && !Motion.Reduce)
        {
            if (!this.AnimationIsRunning(SweepAnimation))
                new Animation(t => { _drawable.Sweep = t * 360; Invalidate(); })
                    .Commit(this, SweepAnimation, length: TurnLength, easing: Easing.Linear, repeat: () => true);
            return;
        }
        this.AbortAnimation(SweepAnimation);
        _drawable.Sweep = null;
        Invalidate();
    }

    private sealed class RadarDrawable : IDrawable
    {
        // lucide-static 1.55.0 icons/radar.svg (ISC) on its 24-unit grid, centred on (12, 12).
        private static readonly PathF[] Rings =
        [
            PathBuilder.Build("M19.07 4.93A10 10 0 0 0 6.99 3.34"),
            PathBuilder.Build("M2.29 9.62A10 10 0 1 0 21.31 8.35"),
            PathBuilder.Build("M16.24 7.76A6 6 0 1 0 8.23 16.67"),
            PathBuilder.Build("M17.99 11.66A6 6 0 0 1 15.77 16.67"),
        ];

        // Each blip with its clockwise angle round the centre; the sweep rests at -45°.
        private static readonly (PointF At, double Degrees)[] Blips = [(new(4, 6), -143.13), (new(12, 18), 90)];
        private const double SweepRest = -45;
        private const float BlipFloor = 0.3f;

        /// <summary>Degrees turned clockwise from rest; null draws the still icon.</summary>
        public double? Sweep { get; set; }

        public void Draw(ICanvas canvas, RectF rect)
        {
            var scale = Math.Min(rect.Width, rect.Height) / 24f;
            canvas.Translate(rect.Center.X - 12 * scale, rect.Center.Y - 12 * scale);
            canvas.Scale(scale, scale);
            var brand = ColorResources.Get("Brand");
            canvas.StrokeColor = brand;
            canvas.StrokeSize = 2;
            canvas.StrokeLineCap = LineCap.Round;
            canvas.StrokeLineJoin = LineJoin.Round;

            foreach (var ring in Rings)
                canvas.DrawPath(ring);
            canvas.DrawCircle(12, 12, 2);

            foreach (var (at, degrees) in Blips)
            {
                var glow = Sweep is { } turned ? RadarBlip.Glow(SweepRest + turned, degrees) : 1;
                canvas.StrokeColor = brand.WithAlpha(BlipFloor + (1 - BlipFloor) * (float)glow);
                canvas.DrawLine(at.X, at.Y, at.X + 0.01f, at.Y);
            }

            canvas.StrokeColor = brand;
            canvas.Rotate((float)(Sweep ?? 0), 12, 12);
            canvas.DrawLine(13.41f, 10.59f, 19.07f, 4.93f);
        }
    }
}
