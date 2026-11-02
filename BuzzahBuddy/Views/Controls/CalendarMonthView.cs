using BuzzahBuddy.Helpers;
using BuzzahBuddy.Models;
using BuzzahBuddy.Messages;
using CommunityToolkit.Mvvm.Messaging;

namespace BuzzahBuddy.Views.Controls;

/// <summary>
/// The History calendar: the month's title over a 7-column grid of level cells. Each day is a
/// <c>SurfaceSunken</c> well with a <c>Brand</c> fill rising to its time, a full well being
/// 4 hours (as <see cref="MeterBar"/>), with the day number below. Today has an <c>Ink</c>
/// ring; days outside the period (future days among them) are a dashed outline left out of
/// the accessibility tree. Each other
/// day reads as one element, its <see cref="CalendarDay.Spoken"/> text.
/// </summary>
/// <remarks>
/// The cells are built once and refilled on each month, and the grid always has six week
/// rows, so paging never rebuilds the view or moves Previous and Next under the finger. A new
/// month fades through (<c>DurationFast</c> out, <c>DurationBase</c> in); under Reduce Motion it is instant.
/// </remarks>
public sealed class CalendarMonthView : ContentView
{
    public static readonly BindableProperty MonthProperty = BindableProperty.Create(
        nameof(Month), typeof(CalendarMonth), typeof(CalendarMonthView), null, propertyChanged: (b, _, _) => ((CalendarMonthView)b).OnMonthChanged());

    /// <summary>The largest well; narrower phones shrink it to fit seven columns.</summary>
    private const double MaxWellSize = 34;

    /// <summary>The today ring's stroke plus its gap, on each side of the well.</summary>
    private const double RingInset = 4;

    /// <summary>The day number sits this close under its well, so it reads as the well's label.</summary>
    private const double NumberGap = 2;

    /// <summary>A day with any time shows at least this much fill.</summary>
    private const double MinFill = 3;

    /// <summary>Enough week rows for any month: a 31-day month starting on the last weekday.</summary>
    private const int WeekRows = 6;

    private readonly VerticalStackLayout _body;
    private readonly Label _title = new() { LineHeight = 1 };
    private readonly Grid _grid = new();
    private readonly Label[] _weekdays = new Label[7];
    private readonly DayCell[] _cells = new DayCell[7 * WeekRows];
    private double _wellSize = MaxWellSize;
    private CalendarMonth? _shown;
    private bool _fading;

    public CalendarMonthView()
    {
        _title.SetDynamicResource(StyleProperty, "TypeTitleM");

        for (var i = 0; i < 7; i++)
            _grid.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Star));
        for (var r = 0; r <= WeekRows; r++)
            _grid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
        _grid.SetDynamicResource(Grid.RowSpacingProperty, "Space3");
        _grid.SetDynamicResource(Grid.ColumnSpacingProperty, "Space2");

        for (var i = 0; i < 7; i++)
        {
            var name = new Label { HorizontalTextAlignment = TextAlignment.Center };
            name.SetDynamicResource(StyleProperty, "TypeCaption");
            // The style's line height adds its extra space above the line on iOS.
            name.LineHeight = 1;
            name.SetDynamicResource(Label.FontFamilyProperty, "FontSans700");
            name.SetDynamicResource(Label.TextColorProperty, "InkMuted");
            AutomationProperties.SetIsInAccessibleTree(name, false);
            _weekdays[i] = name;
            _grid.Add(name, i, 0);
        }

        for (var i = 0; i < _cells.Length; i++)
        {
            _cells[i] = new DayCell(_wellSize);
            _grid.Add(_cells[i].Root, i % 7, 1 + i / 7);
        }

        _body = new VerticalStackLayout { Children = { _title, _grid } };
        _body.SetDynamicResource(VerticalStackLayout.SpacingProperty, "Space4");
        Content = _body;
    }

    public CalendarMonth? Month { get => (CalendarMonth?)GetValue(MonthProperty); set => SetValue(MonthProperty, value); }

    // The first month (and every month under Reduce Motion) shows at once. Months paged to while
    // a fade runs aren't faded one by one: the fade shows the newest when it gets there.
    private async void OnMonthChanged()
    {
        if (_fading)
            return;
        if (Motion.Reduce || _shown is null || Month is null || Handler is null)
        {
            Show(Month);
            return;
        }

        _fading = true;
        // The new month comes in a little slower than the old one goes, so it reads as a fade.
        var fast = (uint)(int)Application.Current!.Resources["DurationFast"];
        var slower = (uint)(int)Application.Current!.Resources["DurationBase"];
        try
        {
            while (!ReferenceEquals(_shown, Month))
            {
                await _body.FadeToAsync(0, fast, Motion.Standard);
                Show(Month);
                await _body.FadeToAsync(1, slower, Motion.Standard);
            }
        }
        finally
        {
            _body.Opacity = 1;
            _fading = false;
        }
    }

    private void Show(CalendarMonth? month)
    {
        _shown = month;
        _title.Text = month?.Title;
        for (var i = 0; i < 7; i++)
            _weekdays[i].Text = month?.WeekdayNames[i];
        for (var i = 0; i < _cells.Length; i++)
        {
            var day = i - (month?.LeadingBlanks ?? 0);
            _cells[i].Show(month is not null && day >= 0 && day < month.Days.Count ? month.Days[day] : null, _wellSize);
        }
    }

    // Fitted while measuring, not after layout, so the page's measure of the card (which holds
    // its size, see HistoryPage) already has the final wells.
    protected override Size MeasureOverride(double widthConstraint, double heightConstraint)
    {
        FitWells(widthConstraint);
        return base.MeasureOverride(widthConstraint, heightConstraint);
    }

    // Wells are 34 square where seven fit; smaller phones shrink them to the column.
    private void FitWells(double width)
    {
        if (width <= 0 || double.IsInfinity(width))
            return;
        var column = (width - 6 * _grid.ColumnSpacing) / 7;
        var size = Math.Min(MaxWellSize, Math.Floor(column));
        if (size == _wellSize)
            return;
        _wellSize = size;
        foreach (var cell in _cells)
            cell.Fit(size);
    }

    /// <summary>One day's slot: its well, fill and today ring in one drawn view, and its number, refilled for each month.</summary>
    /// <remarks>
    /// ponytail: drawn, not built from Borders. Three Borders a day made the calendar about 300 native
    /// views, nearly half the History tab's first-open time on iOS; a GraphicsView is one plain view.
    /// </remarks>
    private sealed class DayCell : IDrawable
    {
        // The well's corner is RadiusSm (8); today's ring rounds a little wider (10). Keep them in
        // step with RadiusSm by hand.
        private const float WellRadius = 8;
        private const float RingRadius = 10;
        private const float RingStroke = 2;

        private readonly GraphicsView _canvas = new() { BackgroundColor = Colors.Transparent };
        private readonly Label _number = new() { HorizontalTextAlignment = TextAlignment.Center, LineHeight = 1 };
        private double _wellSize;
        private double _fraction;
        private bool _outside = true;
        private bool _today;

        public DayCell(double wellSize)
        {
            _canvas.Drawable = this;
            // Today's ring (2 stroke + 2 gap) takes no layout room: the canvas reaches RingInset past
            // the well on each side and negative margins keep its footprint the well's, so the ring
            // spills into the row and column gaps and no other day needs space for it.
            _canvas.Margin = -RingInset;
            _canvas.HorizontalOptions = LayoutOptions.Center;
            _canvas.InputTransparent = true;
            Fit(wellSize);
            // Weak: needs no unregistering.
            WeakReferenceMessenger.Default.Register<GraphicsView, ThemeChangedMessage>(_canvas, (canvas, _) => canvas.Invalidate());

            _number.SetDynamicResource(StyleProperty, "TypeCaption");
            AutomationProperties.SetIsInAccessibleTree(_canvas, false);
            AutomationProperties.SetIsInAccessibleTree(_number, false);
            Root = new VerticalStackLayout { Spacing = NumberGap, Children = { _canvas, _number } };
        }

        public View Root { get; }

        /// <summary>Fills the slot with <paramref name="day"/>; null is a blank before the 1st or after the last day.</summary>
        public void Show(CalendarDay? day, double wellSize)
        {
            // Blanks keep their footprint, so every month is six rows tall.
            Root.Opacity = day is null ? 0 : 1;
            _number.Text = day?.Number;

            _outside = day?.OutsidePeriod != false;
            _fraction = day is { OutsidePeriod: false, HasTime: true } ? day.Fraction : 0;
            _today = day?.IsToday == true;
            _number.SetDynamicResource(Label.FontFamilyProperty, _today ? "FontSans700" : "FontSans400");
            _number.SetDynamicResource(Label.TextColorProperty, _today ? "Ink" : "InkMuted");

            // IsInAccessibleTree on a container leaves its children (the number) in the tree.
            AutomationProperties.SetExcludedWithChildren(Root, day is null || day.OutsidePeriod);
            SemanticProperties.SetDescription(Root, day is { OutsidePeriod: false } ? day.Spoken : null);
            Fit(wellSize);
        }

        public void Fit(double wellSize)
        {
            _wellSize = wellSize;
            _canvas.WidthRequest = wellSize + 2 * RingInset;
            _canvas.HeightRequest = wellSize + 2 * RingInset;
            _canvas.Invalidate();
        }

        public void Draw(ICanvas canvas, RectF rect)
        {
            var hairline = (float)(double)Application.Current!.Resources["BorderHairline"];
            var size = (float)_wellSize;
            var well = new RectF((float)RingInset, (float)RingInset, size, size);
            // Strokes are centred on their path, so each is drawn half its width inside its edge.
            var edge = well.Inflate(-hairline / 2, -hairline / 2);
            canvas.StrokeSize = hairline;

            if (_outside)
            {
                // Outside the period: a dashed outline, no fill.
                canvas.StrokeColor = ColorResources.Get("Line");
                canvas.StrokeDashPattern = [4, 3];
                canvas.DrawRoundedRectangle(edge, WellRadius - hairline / 2);
                canvas.StrokeDashPattern = null;
            }
            else
            {
                canvas.FillColor = ColorResources.Get("SurfaceSunken");
                canvas.FillRoundedRectangle(well, WellRadius);

                // The fill sits inside the hairline stroke, so a full day fills the inner well, not
                // the outer size: scaled to the outer size, 3h 46min filled it.
                var inner = well.Inflate(-hairline, -hairline);
                if (_fraction > 0)
                {
                    var height = (float)Math.Max(MinFill, _fraction * inner.Height);
                    canvas.SaveState();
                    var clip = new PathF();
                    clip.AppendRoundedRectangle(inner, WellRadius - hairline);
                    canvas.ClipPath(clip);
                    canvas.FillColor = ColorResources.Get("Brand");
                    canvas.FillRectangle(inner.X, inner.Bottom - height, inner.Width, height);
                    canvas.RestoreState();
                }

                canvas.StrokeColor = ColorResources.Get("LineStrong");
                canvas.DrawRoundedRectangle(edge, WellRadius - hairline / 2);
            }

            if (_today)
            {
                canvas.StrokeColor = ColorResources.Get("Ink");
                canvas.StrokeSize = RingStroke;
                canvas.DrawRoundedRectangle(rect.Inflate(-RingStroke / 2, -RingStroke / 2), RingRadius - RingStroke / 2);
            }
        }
    }
}
