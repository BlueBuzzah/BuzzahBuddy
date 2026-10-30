using BuzzahBuddy.Models;
using Microsoft.Maui.Controls.Shapes;

namespace BuzzahBuddy.Views.Controls;

/// <summary>
/// The History calendar: one month as a 7-column grid of level cells. Each day is a
/// <c>SurfaceSunken</c> well with a <c>Brand</c> fill rising to its time, a full well being
/// 4 hours (as <see cref="MeterBar"/>), with the day number below. Today has an <c>Ink</c>
/// ring; future days are a dashed outline left out of the accessibility tree. Each other
/// day reads as one element, its <see cref="CalendarDay.Spoken"/> text.
/// </summary>
public sealed class CalendarMonthView : ContentView
{
    public static readonly BindableProperty MonthProperty = BindableProperty.Create(
        nameof(Month), typeof(CalendarMonth), typeof(CalendarMonthView), null, propertyChanged: (b, _, _) => ((CalendarMonthView)b).Rebuild());

    /// <summary>The largest well; narrower phones shrink it to fit seven columns.</summary>
    private const double MaxWellSize = 34;

    /// <summary>The today ring's stroke plus its gap, on each side of the well.</summary>
    private const double RingInset = 4;

    /// <summary>The day number sits this close under its well, so it reads as the well's label.</summary>
    private const double NumberGap = 2;

    /// <summary>A day with any time shows at least this much fill.</summary>
    private const double MinFill = 3;

    private readonly Grid _grid = new();
    private readonly List<(Border Well, BoxView? Fill, double Fraction)> _wells = [];
    private double _wellSize = MaxWellSize;

    public CalendarMonthView()
    {
        for (var i = 0; i < 7; i++)
            _grid.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Star));
        _grid.SetDynamicResource(Grid.RowSpacingProperty, "Space3");
        _grid.SetDynamicResource(Grid.ColumnSpacingProperty, "Space2");
        _grid.SizeChanged += (_, _) => FitWells();
        Content = _grid;
    }

    public CalendarMonth? Month { get => (CalendarMonth?)GetValue(MonthProperty); set => SetValue(MonthProperty, value); }

    private void Rebuild()
    {
        _grid.Children.Clear();
        _grid.RowDefinitions.Clear();
        _wells.Clear();
        if (Month is not { } month)
            return;

        var cells = month.LeadingBlanks + month.Days.Count;
        var rows = 1 + (cells + 6) / 7;
        for (var r = 0; r < rows; r++)
            _grid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));

        for (var i = 0; i < month.WeekdayNames.Count; i++)
        {
            var name = new Label
            {
                Text = month.WeekdayNames[i],
                HorizontalTextAlignment = TextAlignment.Center,
            };
            name.SetDynamicResource(StyleProperty, "TypeCaption");
            // The style's line height adds its extra space above the line on iOS.
            name.LineHeight = 1;
            name.SetDynamicResource(Label.FontFamilyProperty, "FontSans700");
            name.SetDynamicResource(Label.TextColorProperty, "InkMuted");
            AutomationProperties.SetIsInAccessibleTree(name, false);
            _grid.Add(name, i, 0);
        }

        for (var i = 0; i < month.Days.Count; i++)
        {
            var slot = month.LeadingBlanks + i;
            _grid.Add(Cell(month.Days[i]), slot % 7, 1 + slot / 7);
        }

        FitWells();
    }

    private View Cell(CalendarDay day)
    {
        var well = new Border
        {
            StrokeShape = new RoundRectangle { CornerRadius = 8 },
            WidthRequest = _wellSize,
            HeightRequest = _wellSize,
        };
        well.SetDynamicResource(Border.StrokeThicknessProperty, "BorderHairline");
        BoxView? fill = null;
        if (day.IsFuture)
        {
            well.BackgroundColor = Colors.Transparent;
            well.StrokeDashArray = [4, 3];
            well.SetDynamicResource(Border.StrokeProperty, "Line");
        }
        else
        {
            well.SetDynamicResource(BackgroundColorProperty, "SurfaceSunken");
            well.SetDynamicResource(Border.StrokeProperty, "LineStrong");
            fill = new BoxView { VerticalOptions = LayoutOptions.End };
            fill.SetDynamicResource(BoxView.ColorProperty, "Brand");
            well.Content = fill;
        }
        _wells.Add((well, fill, day.HasTime ? day.Fraction : 0));
        SizeFill(fill, day.HasTime ? day.Fraction : 0);

        View top = well;
        well.HorizontalOptions = LayoutOptions.Center;
        if (day.IsToday)
        {
            // Today's ring (2 stroke + 2 gap) takes no layout room: negative margins keep its
            // footprint the well's, so it spills into the row and column gaps and no other
            // day needs space for it. Only today gets a wrapper: a Border with no stroke set
            // still draws one on iOS.
            var ring = new Border
            {
                Padding = 2,
                StrokeThickness = 2,
                StrokeShape = new RoundRectangle { CornerRadius = 10 },
                Margin = -RingInset,
                HorizontalOptions = LayoutOptions.Center,
                Content = well,
            };
            ring.SetDynamicResource(Border.StrokeProperty, "Ink");
            top = ring;
        }

        var number = new Label { Text = day.Number, HorizontalTextAlignment = TextAlignment.Center, LineHeight = 1 };
        number.SetDynamicResource(StyleProperty, "TypeCaption");
        if (day.IsToday)
        {
            number.SetDynamicResource(Label.FontFamilyProperty, "FontSans700");
            number.SetDynamicResource(Label.TextColorProperty, "Ink");
        }
        else
        {
            number.SetDynamicResource(Label.TextColorProperty, "InkMuted");
        }

        var cell = new VerticalStackLayout { Spacing = NumberGap, Children = { top, number } };
        if (day.IsFuture)
        {
            // IsInAccessibleTree on a container leaves its children (the number) in the tree.
            AutomationProperties.SetExcludedWithChildren(cell, true);
        }
        else
        {
            SemanticProperties.SetDescription(cell, day.Spoken);
            AutomationProperties.SetIsInAccessibleTree(top, false);
            AutomationProperties.SetIsInAccessibleTree(number, false);
        }
        return cell;
    }

    // Wells are 34 square where seven fit; smaller phones shrink them to the column.
    private void FitWells()
    {
        if (_grid.Width <= 0)
            return;
        var column = (_grid.Width - 6 * _grid.ColumnSpacing) / 7;
        var size = Math.Min(MaxWellSize, Math.Floor(column));
        if (size == _wellSize)
            return;
        _wellSize = size;
        foreach (var (well, fill, fraction) in _wells)
        {
            well.WidthRequest = size;
            well.HeightRequest = size;
            SizeFill(fill, fraction);
        }
    }

    private void SizeFill(BoxView? fill, double fraction)
    {
        if (fill is not null)
            fill.HeightRequest = fraction > 0 ? Math.Max(MinFill, fraction * _wellSize) : 0;
    }
}
