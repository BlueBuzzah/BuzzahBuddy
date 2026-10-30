using BuzzahBuddy.Helpers;
using BuzzahBuddy.ViewModels;
using BuzzahBuddy.Views.Controls;

namespace BuzzahBuddy.Views;

public partial class HistoryPage : ContentPage
{
    private readonly HistoryViewModel _viewModel;

    public HistoryPage(HistoryViewModel viewModel)
    {
        InitializeComponent();
        _viewModel = viewModel;
        BindingContext = viewModel;
        // Rows come and go with each refresh; each lays itself out on load, and a text-size
        // change while the app was away re-lays them all on resume.
        this.TrackTextScale(() =>
        {
            foreach (var row in WeekRows.Children.OfType<Grid>())
                ArrangeWeekRow(row);
        });
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        _viewModel.RefreshCommand.ExecuteAsync(null).SafeFireAndForget("[HISTORY]");
    }

    private void OnWeekRowLoaded(object? sender, EventArgs e)
    {
        if (sender is Grid row)
            ArrangeWeekRow(row);
    }

    // Label, bar, time on one line; at large text the label and time sit above the bar.
    private static void ArrangeWeekRow(Grid row)
    {
        if (row.Children.Count < 3
            || row.Children[0] is not Label label
            || row.Children[1] is not MeterBar bar
            || row.Children[2] is not Label value)
            return;

        var stacked = OsAccessibility.StackForLargeText;
        if (stacked)
        {
            row.ColumnDefinitions = new ColumnDefinitionCollection(
                new ColumnDefinition(GridLength.Star), new ColumnDefinition(GridLength.Auto));
            row.RowDefinitions = new RowDefinitionCollection(
                new RowDefinition(GridLength.Auto), new RowDefinition(GridLength.Auto));
            Place(label, 0, 0);
            Place(value, 0, 1);
            Place(bar, 1, 0, span: 2);
            label.WidthRequest = -1;
            value.WidthRequest = -1;
            value.HorizontalOptions = LayoutOptions.End;
        }
        else
        {
            row.ColumnDefinitions = new ColumnDefinitionCollection(
                new ColumnDefinition(GridLength.Auto), new ColumnDefinition(GridLength.Star), new ColumnDefinition(GridLength.Auto));
            row.RowDefinitions = new RowDefinitionCollection(new RowDefinition(GridLength.Auto));
            Place(label, 0, 0);
            Place(bar, 0, 1);
            Place(value, 0, 2);
            // Fixed widths on both sides keep every track the same length, so bars compare.
            label.WidthRequest = 88;
            value.WidthRequest = 96;
            value.HorizontalOptions = LayoutOptions.Fill;
        }
    }

    private static void Place(View view, int row, int column, int span = 1)
    {
        Grid.SetRow(view, row);
        Grid.SetColumn(view, column);
        Grid.SetColumnSpan(view, span);
    }
}
