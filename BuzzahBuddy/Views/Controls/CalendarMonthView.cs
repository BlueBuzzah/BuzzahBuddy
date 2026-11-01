using BuzzahBuddy.Helpers;
using BuzzahBuddy.Models;
using Microsoft.Maui.Controls.Shapes;

namespace BuzzahBuddy.Views.Controls;

/// <summary>
/// The History calendar: the month's title over a 7-column grid of level cells. Each day is a
/// <c>SurfaceSunken</c> well with a <c>Brand</c> fill rising to its time, a full well being
/// 4 hours (as <see cref="MeterBar"/>), with the day number below. Today has an <c>Ink</c>
/// ring; future days are a dashed outline left out of the accessibility tree. Each other
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

    /// <summary>One day's slot: its well, fill, today ring and number, refilled for each month.</summary>
    private sealed class DayCell
    {
        private readonly Grid _top = [];
        private readonly Border _well;
        private readonly ContentView _fill = new() { VerticalOptions = LayoutOptions.End };
        private readonly Border _ring;
        private readonly Label _number = new() { HorizontalTextAlignment = TextAlignment.Center, LineHeight = 1 };
        private double _fraction;

        public DayCell(double wellSize)
        {
            // The well's corner is RadiusSm (8), and today's ring below rounds a little wider (10).
            // Both are written out: a StrokeShape never joins the visual tree, so a DynamicResource
            // set on it has nothing to resolve against. Keep them in step with RadiusSm by hand.
            _well = new Border { StrokeShape = new RoundRectangle { CornerRadius = 8 }, Content = _fill };
            _well.SetDynamicResource(Border.StrokeThicknessProperty, "BorderHairline");
            // A plain view's background, not a BoxView: iOS draws a BoxView once and doesn't redraw
            // it when it grows from zero, which left a day paged to with no fill drawn.
            _fill.SetDynamicResource(BackgroundColorProperty, "Brand");

            // Today's ring (2 stroke + 2 gap) takes no layout room: negative margins keep its
            // footprint the well's, so it spills into the row and column gaps and no other day
            // needs space for it. It is hidden on every other day: a Border with no stroke set
            // still draws one on iOS.
            _ring = new Border
            {
                StrokeThickness = 2,
                StrokeShape = new RoundRectangle { CornerRadius = 10 },
                Margin = -RingInset,
                InputTransparent = true,
                IsVisible = false,
            };
            _ring.SetDynamicResource(Border.StrokeProperty, "Ink");

            // The fixed-size slot holds the well and ring, so a fill that changes height stays inside it.
            _top.HorizontalOptions = LayoutOptions.Center;
            _top.Add(_well);
            _top.Add(_ring);
            Fit(wellSize);

            _number.SetDynamicResource(StyleProperty, "TypeCaption");
            AutomationProperties.SetIsInAccessibleTree(_top, false);
            AutomationProperties.SetIsInAccessibleTree(_number, false);
            Root = new VerticalStackLayout { Spacing = NumberGap, Children = { _top, _number } };
        }

        public View Root { get; }

        /// <summary>Fills the slot with <paramref name="day"/>; null is a blank before the 1st or after the last day.</summary>
        public void Show(CalendarDay? day, double wellSize)
        {
            // Blanks keep their footprint, so every month is six rows tall.
            Root.Opacity = day is null ? 0 : 1;
            _number.Text = day?.Number;

            // Both looks use resources: a value set directly outranks a later DynamicResource, so a
            // future day's well set to Transparent would never take SurfaceSunken back once the
            // slot shows a past day. SurfaceRaised is the card's own color.
            var future = day?.IsFuture != false;
            _well.SetDynamicResource(BackgroundColorProperty, future ? "SurfaceRaised" : "SurfaceSunken");
            _well.StrokeDashArray = future ? [4, 3] : null;
            _well.SetDynamicResource(Border.StrokeProperty, future ? "Line" : "LineStrong");
            _fraction = day is { IsFuture: false, HasTime: true } ? day.Fraction : 0;
            SizeFill(wellSize);

            var today = day?.IsToday == true;
            _ring.IsVisible = today;
            _number.SetDynamicResource(Label.FontFamilyProperty, today ? "FontSans700" : "FontSans400");
            _number.SetDynamicResource(Label.TextColorProperty, today ? "Ink" : "InkMuted");

            // IsInAccessibleTree on a container leaves its children (the number) in the tree.
            AutomationProperties.SetExcludedWithChildren(Root, day is null || day.IsFuture);
            SemanticProperties.SetDescription(Root, day is { IsFuture: false } ? day.Spoken : null);
        }

        public void Fit(double wellSize)
        {
            _top.WidthRequest = wellSize;
            _top.HeightRequest = wellSize;
            SizeFill(wellSize);
        }

        // The fill sits inside the well's hairline stroke, so a full day fills the inner well, not the
        // outer size: scaled to the outer size, 3h 46min filled it.
        private void SizeFill(double wellSize)
        {
            var inner = wellSize - 2 * (double)Application.Current!.Resources["BorderHairline"];
            _fill.HeightRequest = _fraction > 0 ? Math.Max(MinFill, _fraction * inner) : 0;
        }
    }
}
