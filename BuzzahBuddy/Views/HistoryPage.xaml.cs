using BuzzahBuddy.Helpers;
using BuzzahBuddy.Models;
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
        // A text-size change while the app was away re-lays the page on resume. Trend rows
        // come and go with the period; each also lays itself out on load.
        this.TrackTextScale(() =>
        {
            _viewModel.LargeText = OsAccessibility.StackForLargeText;
            ArrangeTiles(_viewModel.LargeText);
            foreach (var row in TrendRowList.Children.OfType<Grid>())
                ArrangeTrendRow(row);
        });
#if IOS
        _viewModel.PropertyChanging += OnViewModelChanging;
#endif
        _viewModel.DataSourceChanged += (_, _) => ScrollToTop();
        SummaryScroll.GroupForScreenReader();
        DayList.GroupForScreenReader();
        this.TrackLayout(ApplyLayout);
    }

    // The page starts as one column: SummaryPanel in the list header (XAML).
    private bool _twoPane;

    // Wide windows: the summary scrolls on its own in the left pane and the day list takes the right.
    // Otherwise one list with the summary in its header, in a centered column. SummaryPanel is one
    // instance moved between the two, so its bindings and AutomationIds stay intact; it moves only
    // when the mode changes, since re-attaching the list header can reset the scroll position.
    private void ApplyLayout(PageLayout layout)
    {
        var two = layout.TwoPane;
        var cardBottom = 24;
        if (two)
        {
            var gap = (double)Application.Current!.Resources["Space8"];
            // Equal content widths; the right column also holds its own gutter, inside the list.
            var pane = Math.Max(0, (layout.Width - 2 * layout.Gutter - gap) / 2);
            PageGrid.Padding = new Thickness(layout.Gutter, 0, 0, 0);
            PageGrid.ColumnSpacing = gap;
            PageGrid.ColumnDefinitions = new ColumnDefinitionCollection(
                new ColumnDefinition(new GridLength(pane)), new ColumnDefinition(GridLength.Star));
            Grid.SetColumn(DayList, 1);
            Grid.SetColumnSpan(DayList, 1);
            ListHeader.Padding = new Thickness(0, 0, layout.Gutter, 12);
            Resources["DayCardMargin"] = new Thickness(0, 0, layout.Gutter, cardBottom);
            // Don't let the summary stretch past what reads well.
            var cap = AdaptiveLayout.ColumnMaxWidth * Math.Max(1, OsAccessibility.TextScale);
            SummaryPanel.WidthRequest = pane > cap ? cap : -1;
            SummaryPanel.HorizontalOptions = pane > cap ? LayoutOptions.Start : LayoutOptions.Fill;
        }
        else
        {
            var side = Math.Max(0, (layout.Width - layout.ColumnWidth) / 2);
            PageGrid.Padding = new Thickness(0);
            PageGrid.ColumnSpacing = 0;
            PageGrid.ColumnDefinitions = new ColumnDefinitionCollection(new ColumnDefinition(GridLength.Star));
            Grid.SetColumn(DayList, 0);
            Grid.SetColumnSpan(DayList, 1);
            ListHeader.Padding = new Thickness(side, 0, side, 12);
            Resources["DayCardMargin"] = new Thickness(side, 0, side, cardBottom);
            SummaryPanel.WidthRequest = -1;
            SummaryPanel.HorizontalOptions = LayoutOptions.Fill;
        }

        if (two == _twoPane)
            return;
        _twoPane = two;
        if (two)
        {
            ListHeader.Remove(SummaryPanel);
            SummaryScroll.Content = SummaryPanel;
        }
        else
        {
            SummaryScroll.Content = null;
            ListHeader.Insert(0, SummaryPanel);
        }
        SummaryScroll.IsVisible = two;
    }

    // The list header (History's title) is the first row of the platform list on both platforms.
    private void ScrollToTop()
    {
#if IOS
        if (DayList.Handler?.PlatformView is not UIKit.UIView root || FindList(root) is not { } list)
            return;
        var top = new CoreGraphics.CGPoint(0, -list.AdjustedContentInset.Top);
        // A header change in the same refresh is being held at the old offset; hold the top instead.
        if (_heldOffset is not null)
            _heldOffset = top;
        else
            list.SetContentOffset(top, animated: false);
#elif ANDROID
        if (DayList.Handler?.PlatformView is AndroidX.RecyclerView.Widget.RecyclerView list)
            list.ScrollToPosition(0);
#endif
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        _viewModel.RefreshCommand.ExecuteAsync(null).SafeFireAndForget("[HISTORY]");
    }

    // Overview tiles two by two; one column at large text. Laid out only when that changes: any
    // relayout invalidates the list header, which on iOS moves the page (see OnViewModelChanging),
    // and the text-size check re-runs every time the app resumes.
    private void ArrangeTiles(bool stacked)
    {
        if (TileGrid.ColumnDefinitions.Count == (stacked ? 1 : 2) && TileGrid.RowDefinitions.Count == (stacked ? 4 : 2))
            return;
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

    private void OnTrendRowLoaded(object? sender, EventArgs e)
    {
        if (sender is Grid row)
            ArrangeTrendRow(row);
    }

    // Label and time on one line, the bar under them; at large text the label gets the full
    // width and the time moves under it, so short labels like "Oct 1-2" don't wrap.
    private static void ArrangeTrendRow(Grid row)
    {
        if (row.Children.Count < 3
            || row.Children[0] is not Label label
            || row.Children[1] is not Label value
            || row.Children[2] is not MeterBar bar)
            return;

        var stacked = OsAccessibility.StackForLargeText;
        // Already laid out this way: leave it, so a resume doesn't invalidate the list header.
        if (row.RowDefinitions.Count == (stacked ? 3 : 2) && Grid.GetRow(value) == (stacked ? 1 : 0))
            return;
        row.RowDefinitions = new RowDefinitionCollection(
            Enumerable.Range(0, stacked ? 3 : 2).Select(_ => new RowDefinition(GridLength.Auto)).ToArray());
        Place(label, 0, 0, span: stacked ? 2 : 1);
        Place(value, stacked ? 1 : 0, stacked ? 0 : 1, span: stacked ? 2 : 1);
        Place(bar, stacked ? 2 : 1, 0, span: 2);
        value.HorizontalTextAlignment = stacked ? TextAlignment.Start : TextAlignment.End;
    }

    private static void Place(View view, int row, int column, int span = 1)
    {
        Grid.SetRow(view, row);
        Grid.SetColumn(view, column);
        Grid.SetColumnSpan(view, span);
    }

#if IOS
    // ponytail: on iOS any change inside the list header (another month, another period)
    // invalidates the whole list layout (MAUI Items2 StructuredItemsViewController2
    // .InvalidateLayoutIfItemsMeasureChanged). The header's height drops back to its estimate,
    // and while it is scrolled out of view it is never measured again, so the day cards slide
    // up under an unchanged offset and the page seems to jump far down. After each batch of
    // header changes, bring the header on screen for one layout pass so it measures itself,
    // then return to the same offset, all before the next frame is drawn. When the header was
    // wholly scrolled away (the person is in the day list), its change in height is added too,
    // so the day cards stay still (a Demo banner appearing, a period change). The header can be
    // invalidated again a pass or two later (new trend rows lay themselves out on load), so the
    // passes repeat until the header and content heights stop changing. Days is the list itself,
    // not the header, so it keeps the list's own behavior.
    private const int MaxRestorePasses = 6;
    private CoreGraphics.CGPoint? _heldOffset;
    private double _heldHeaderHeight;

    private void OnViewModelChanging(object? sender, System.ComponentModel.PropertyChangingEventArgs e)
    {
        if (_heldOffset is not null
            || _twoPane // the header holds only the Days heading then, so nothing to hold still
            || e.PropertyName is nameof(HistoryViewModel.Days) or nameof(HistoryViewModel.IsBusy)
                or nameof(HistoryViewModel.IsRefreshing) or nameof(HistoryViewModel.Title)
            || DayList.Handler?.PlatformView is not UIKit.UIView root
            || FindList(root) is not { } list)
            return;

        _heldOffset = list.ContentOffset;
        _heldHeaderHeight = ListHeader.Height;
        Dispatcher.Dispatch(() => RestoreOffset(root, list, pass: 1, lastHeader: -1, lastContent: -1));
    }

    private void RestoreOffset(UIKit.UIView root, UIKit.UICollectionView list, int pass, double lastHeader, double lastContent)
    {
        if (_heldOffset is not { } held)
            return;
        // A content height that dropped back to the estimate since the last pass means the header
        // was invalidated again.
        var invalidated = (double)list.ContentSize.Height != lastContent;
        var top = -list.AdjustedContentInset.Top;
        list.SetContentOffset(new CoreGraphics.CGPoint(held.X, top), animated: false);
        root.LayoutIfNeeded();
        double y = held.Y;
        if (_heldHeaderHeight > 0 && (double)(held.Y - top) >= _heldHeaderHeight)
            y += ListHeader.Height - _heldHeaderHeight;
        var bottom = list.ContentSize.Height + list.AdjustedContentInset.Bottom - list.Bounds.Height;
        list.SetContentOffset(new CoreGraphics.CGPoint(held.X, Math.Max(top, Math.Min((nfloat)y, bottom))), animated: false);
        root.LayoutIfNeeded();
        var header = ListHeader.Height;
        var content = (double)list.ContentSize.Height;
        var settled = pass > 1 && !invalidated && header == lastHeader && content == lastContent;
        if (settled || pass == MaxRestorePasses)
            _heldOffset = null;
        else
            Dispatcher.Dispatch(() => RestoreOffset(root, list, pass + 1, header, content));
    }

    private static UIKit.UICollectionView? FindList(UIKit.UIView view)
    {
        if (view is UIKit.UICollectionView list)
            return list;
        foreach (var child in view.Subviews)
        {
            if (FindList(child) is { } found)
                return found;
        }
        return null;
    }
#endif
}
