using BuzzahBuddy.Helpers;
using BuzzahBuddy.Messages;
using BuzzahBuddy.Models;
using CommunityToolkit.Mvvm.Messaging;

namespace BuzzahBuddy.Views.Controls;

/// <summary>
/// The session countdown ring. Running: accent arc on a sunken track. Paused: line-strong
/// arc and a pause icon. Complete: full success arc and a check. Idle: no arc, and the
/// track breathes between surface-sunken and brand-soft (static under reduced motion).
/// </summary>
public partial class SessionRing : ContentView
{
    private const float Stroke = 14f;
    private const string BreathAnimation = "SessionRingBreath";

    public static readonly BindableProperty StateProperty = BindableProperty.Create(
        nameof(State), typeof(SessionRingState), typeof(SessionRing), SessionRingState.Idle, propertyChanged: Update);

    public static readonly BindableProperty ProgressProperty = BindableProperty.Create(
        nameof(Progress), typeof(double), typeof(SessionRing), 0.0, propertyChanged: Update);

    public static readonly BindableProperty TimeProperty = BindableProperty.Create(
        nameof(Time), typeof(string), typeof(SessionRing), string.Empty, propertyChanged: Update);

    public static readonly BindableProperty AccessibleTextProperty = BindableProperty.Create(
        nameof(AccessibleText), typeof(string), typeof(SessionRing), string.Empty,
        propertyChanged: (b, _, n) => SemanticProperties.SetDescription(((SessionRing)b).Root, (string)n));

    private readonly RingDrawable _drawable = new();
    private double _baseSize;

    public SessionRing()
    {
        InitializeComponent();
        Canvas.Drawable = _drawable;
        TimeText.SizeChanged += (_, _) => FitSize();
        Loaded += (_, _) =>
        {
            WeakReferenceMessenger.Default.Register<SessionRing, ThemeChangedMessage>(this, (ring, _) => ring.Canvas.Invalidate());
            Apply();
        };
        Unloaded += (_, _) =>
        {
            WeakReferenceMessenger.Default.Unregister<ThemeChangedMessage>(this);
            this.AbortAnimation(BreathAnimation);
        };
        SetBaseSize();
    }

    public SessionRingState State { get => (SessionRingState)GetValue(StateProperty); set => SetValue(StateProperty, value); }

    /// <summary>Share of the session done, 0–1.</summary>
    public double Progress { get => (double)GetValue(ProgressProperty); set => SetValue(ProgressProperty, value); }

    /// <summary>Time in the middle, from <see cref="DurationText.Clock"/>.</summary>
    public string Time { get => (string)GetValue(TimeProperty); set => SetValue(TimeProperty, value); }

    /// <summary>The one label screen readers hear; the owner throttles it to once a minute.</summary>
    public string AccessibleText { get => (string)GetValue(AccessibleTextProperty); set => SetValue(AccessibleTextProperty, value); }

    private static void Update(BindableObject bindable, object oldValue, object newValue) => ((SessionRing)bindable).Apply();

    /// <summary>280, or 240 on phones under 360 wide (SessionRing README).</summary>
    private void SetBaseSize()
    {
        var display = DeviceDisplay.MainDisplayInfo;
        var width = display.Density > 0 ? display.Width / display.Density : 400;
        _baseSize = width > 0 && width < 360 ? 240 : 280;
        // bundle.js: under 300 the time shrinks to 20% of the ring.
        TimeText.FontSize = Math.Round(_baseSize * 0.2);
        SetSize(_baseSize);
    }

    private void SetSize(double size)
    {
        Root.WidthRequest = size;
        Root.HeightRequest = size;
    }

    /// <summary>At large text sizes the ring grows to fit the time instead of clipping it.</summary>
    private void FitSize()
    {
        var needed = Math.Max(_baseSize, Math.Ceiling(TimeText.Width + 2 * Stroke + 48));
        if (Math.Abs(needed - Root.WidthRequest) > 1)
            SetSize(needed);
    }

    private void Apply()
    {
        TimeText.Text = Time;
        StateText.Text = SessionRingText.Word(State);
        StateGlyph.Text = State switch
        {
            SessionRingState.Paused => LucideIcons.Pause,
            SessionRingState.Complete => LucideIcons.CircleCheck,
            _ => null,
        };
        StateGlyph.IsVisible = StateGlyph.Text is not null;

        _drawable.State = State;
        _drawable.Progress = Math.Clamp(Progress, 0, 1);

        if (State == SessionRingState.Idle && !Motion.Reduce)
        {
            if (!this.AnimationIsRunning(BreathAnimation))
            {
                var pulse = (int)Application.Current!.Resources["DurationPulse"];
                // One breath out and back over two pulses, eased like CSS "alternate".
                new Animation(t => { _drawable.Breath = (1 - Math.Cos(2 * Math.PI * t)) / 2; Canvas.Invalidate(); })
                    .Commit(this, BreathAnimation, length: (uint)(pulse * 2), repeat: () => true);
            }
        }
        else
        {
            this.AbortAnimation(BreathAnimation);
            _drawable.Breath = State == SessionRingState.Idle ? 1 : 0; // reduced motion: static brand-soft halo
        }
        Canvas.Invalidate();
    }

    private sealed class RingDrawable : IDrawable
    {
        public SessionRingState State { get; set; }
        public double Progress { get; set; }
        public double Breath { get; set; }

        public void Draw(ICanvas canvas, RectF rect)
        {
            var size = Math.Min(rect.Width, rect.Height);
            var radius = (size - Stroke) / 2 - 6;
            if (radius <= 0)
                return;
            var x = rect.Center.X - radius;
            var y = rect.Center.Y - radius;
            var diameter = radius * 2;
            var highContrast = Application.Current?.Resources.TryGetValue("HcBorderWidth", out var hc) == true && hc is double w && w > 0;

            canvas.StrokeSize = Stroke;
            canvas.StrokeLineCap = LineCap.Round;
            var track = ColorResources.Get(highContrast ? "Line" : "SurfaceSunken");
            if (State == SessionRingState.Idle)
                track = Lerp(track, ColorResources.Get("BrandSoft"), (float)Breath);
            canvas.StrokeColor = track;
            canvas.DrawEllipse(x, y, diameter, diameter);

            var (arcKey, fraction) = State switch
            {
                SessionRingState.Running => ("Accent", Progress),
                SessionRingState.Paused => ("LineStrong", Progress),
                SessionRingState.Complete => ("Success", 1.0),
                _ => (null, 0.0),
            };
            if (arcKey is null || fraction <= 0)
                return;
            canvas.StrokeColor = ColorResources.Get(arcKey);
            if (fraction >= 0.999)
                canvas.DrawEllipse(x, y, diameter, diameter); // a 360° arc degenerates
            else
                canvas.DrawArc(x, y, diameter, diameter, 90f, 90f - (float)(fraction * 360), clockwise: true, closed: false);
        }

        private static Color Lerp(Color a, Color b, float t) =>
            new(a.Red + (b.Red - a.Red) * t, a.Green + (b.Green - a.Green) * t, a.Blue + (b.Blue - a.Blue) * t);
    }
}
