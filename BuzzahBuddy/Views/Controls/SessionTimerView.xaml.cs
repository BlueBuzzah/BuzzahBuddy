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
