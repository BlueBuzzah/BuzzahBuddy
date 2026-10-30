using BuzzahBuddy.Helpers;
using BuzzahBuddy.ViewModels;

namespace BuzzahBuddy.Views;

public partial class HistoryPage : ContentPage
{
    private readonly HistoryViewModel _viewModel;

    public HistoryPage(HistoryViewModel viewModel)
    {
        InitializeComponent();
        _viewModel = viewModel;
        BindingContext = viewModel;
        // A text-size change while the app was away re-lays the page on resume.
        this.TrackTextScale(() =>
        {
            _viewModel.LargeText = OsAccessibility.StackForLargeText;
            ArrangeTiles(_viewModel.LargeText);
        });
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        _viewModel.RefreshCommand.ExecuteAsync(null).SafeFireAndForget("[HISTORY]");
    }

    // Overview tiles two by two; one column at large text.
    private void ArrangeTiles(bool stacked)
    {
        var space = (double)Application.Current!.Resources["Space3"];
        TileGrid.ColumnSpacing = space;
        TileGrid.RowSpacing = space;
        TileGrid.ColumnDefinitions = stacked
            ? new ColumnDefinitionCollection(new ColumnDefinition(GridLength.Star))
            : new ColumnDefinitionCollection(new ColumnDefinition(GridLength.Star), new ColumnDefinition(GridLength.Star));
        var rows = stacked ? 4 : 2;
        TileGrid.RowDefinitions = new RowDefinitionCollection(
            Enumerable.Range(0, rows).Select(_ => new RowDefinition(GridLength.Auto)).ToArray());
        for (var i = 0; i < TileGrid.Children.Count; i++)
        {
            if (TileGrid.Children[i] is View tile)
                Place(tile, stacked ? i : i / 2, stacked ? 0 : i % 2);
        }
    }

    private static void Place(View view, int row, int column)
    {
        Grid.SetRow(view, row);
        Grid.SetColumn(view, column);
    }
}
