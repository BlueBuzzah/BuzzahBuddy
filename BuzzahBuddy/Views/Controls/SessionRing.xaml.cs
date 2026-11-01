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
    private double _maxSize;
    private double _baseFont;

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
        // A hidden ring stops breathing (the page binds IsVisible to what hides it).
        PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(IsVisible))
                Apply();
        };
        SetDefaultSize();
    }

    public SessionRingState State { get => (SessionRingState)GetValue(StateProperty); set => SetValue(StateProperty, value); }

    /// <summary>Share of the session done, 0–1.</summary>
    public double Progress { get => (double)GetValue(ProgressProperty); set => SetValue(ProgressProperty, value); }

    /// <summary>Time in the middle, from <see cref="DurationText.Clock"/>.</summary>
    public string Time { get => (string)GetValue(TimeProperty); set => SetValue(TimeProperty, value); }

    /// <summary>The one label screen readers hear; the owner throttles it to once a minute.</summary>
    public string AccessibleText { get => (string)GetValue(AccessibleTextProperty); set => SetValue(AccessibleTextProperty, value); }

    private static void Update(BindableObject bindable, object oldValue, object newValue) => ((SessionRing)bindable).Apply();

    /// <summary>
    /// Until the page calls <see cref="Fit"/>: 280, or 240 on phones under 360 wide, with the
    /// screen's 16pt gutters as the limit (SessionRing README).
    /// </summary>
    private void SetDefaultSize()
    {
        var display = DeviceDisplay.MainDisplayInfo;
        var width = display.Density > 0 ? display.Width / display.Density : 400;
        Resize(width > 0 && width < 360 ? 240 : 280, width - 32);
    }

    /// <summary>
    /// Sizes the ring from its space. <paramref name="sizingWidth"/> and <paramref name="paneHeight"/> pick
    /// the base size (<see cref="SessionRingSize.For"/>); <paramref name="availableWidth"/> is the column or
    /// pane width the time may grow into at large text sizes. Safe to call again on rotation.
    /// </summary>
    public void Fit(double sizingWidth, double? paneHeight, double availableWidth)
    {
        // A page that hasn't been laid out yet reports -1 or NaN: keep the current size.
        if (!(sizingWidth > 0) || !(availableWidth > 0) || paneHeight is { } h && !(h > 0))
            return;
        Resize(SessionRingSize.For(sizingWidth, paneHeight), availableWidth);
    }

    private void Resize(double baseSize, double availableWidth)
    {
        _baseSize = baseSize;
        _maxSize = Math.Max(baseSize, availableWidth);
        // bundle.js: under 300 the time shrinks to 20% of the ring.
        _baseFont = Math.Round(baseSize * 0.2);
        FitSize(); // settles font and size right here; it never waits for a SizeChanged
    }

    // Lexend keeps 0.3em empty above its caps (ascender 1.0em, cap height 0.7em) and digits have
    // no descenders, so the time and state word sat low in the ring. Trim that space off the top.
    private void SetTimeFont(double font)
    {
        TimeText.FontSize = font;
        TimeText.Margin = new Thickness(0, -0.3 * font, 0, 0);
    }

    private void SetSize(double size)
    {
        Root.WidthRequest = size;
        Root.HeightRequest = size;
    }

    /// <summary>
    /// At large text sizes the ring grows to fit the time instead of clipping it, up to the
    /// screen's width; past that the time shrinks to fit rather than pushing the ring off screen.
    /// </summary>
    private void FitSize()
    {
        const double inset = 2 * Stroke + 48;
        // The laid-out width is already clipped to the ring, so measure the time unconstrained.
        var natural = TimeText.Measure(double.PositiveInfinity, double.PositiveInfinity).Width;
        if (natural <= 0)
        {
            // Not measurable yet (not attached): the base look; the time's SizeChanged fits it later.
            SetTimeFont(_baseFont);
            SetSize(_baseSize);
            return;
        }
        // Worked out from the base size, so the time grows back if the text size drops again.
        var atBase = natural * _baseFont / TimeText.FontSize;
        var font = Math.Min(_baseFont, Math.Floor(_baseFont * (_maxSize - inset) / atBase));
        if (Math.Abs(font - TimeText.FontSize) >= 1)
        {
            SetTimeFont(font);
            natural = atBase * font / _baseFont; // the width at the new font, without a layout pass
        }
        SetSize(Math.Clamp(Math.Ceiling(natural + inset), _baseSize, _maxSize));
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

        if (State == SessionRingState.Idle && !Motion.Reduce && IsVisible && IsLoaded)
        {
            if (!this.AnimationIsRunning(BreathAnimation))
            {
                var pulse = (int)Application.Current!.Resources["DurationPulse"];
                // One breath out and back over two pulses, eased like CSS "alternate".
                // Each cycle re-checks Reduce Motion, which can be turned on while the app runs.
                new Animation(t => { _drawable.Breath = (1 - Math.Cos(2 * Math.PI * t)) / 2; Canvas.Invalidate(); })
                    .Commit(this, BreathAnimation, length: (uint)(pulse * 2), repeat: () => !Motion.Reduce,
                        finished: (_, cancelled) => { if (!cancelled) Apply(); });
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
