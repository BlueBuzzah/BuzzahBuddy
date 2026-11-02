using BuzzahBuddy.Helpers;
using BuzzahBuddy.Models;
using BuzzahBuddy.ViewModels;

namespace BuzzahBuddy.Views;

public partial class GloveControlPage : ContentPage
{
    private readonly GloveControlViewModel _viewModel;
    private PageLayout? _layout;
    private bool _twoPane;

    public GloveControlPage(GloveControlViewModel viewModel)
    {
        InitializeComponent();
        _viewModel = viewModel;
        BindingContext = viewModel;
        ProfileRow.TrackTextScale(UpdateProfileLayout);
        // The content fills the viewport at least, so its star row (Get Started) can center.
        Scroller.SizeChanged += (_, _) => ScrollContent.MinimumHeightRequest = Scroller.Height;
        // The ring fits the pane's allocated size, so it follows the pane, not a guess.
        RingPane.SizeChanged += (_, _) => FitRing();
        MainColumn.GroupForScreenReader();
        RingPane.GroupForScreenReader();
        this.TrackLayout(ApplyLayout);
        // Two panes only while connected; the page is a singleton and ConnectionInfo is get-only, so one subscription.
        viewModel.ConnectionInfo.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName is "IsConnected" or null or "" && _layout is { } l)
                ApplyLayout(l);
        };
    }

    // Wide landscape: the ring alone in the left pane, everything else in the right. Otherwise one
    // centered column with the ring in its stack. One Ring instance moves between the two, so its
    // bindings, AutomationId and single accessible timer stay intact.
    private void ApplyLayout(PageLayout layout)
    {
        _layout = layout;
        var twoPane = layout.TwoPane && layout.Landscape && _viewModel.ConnectionInfo.IsConnected;
        if (twoPane)
        {
            Root.Padding = new Thickness(layout.Gutter, 0, layout.Gutter, 16);
            Root.ColumnSpacing = (double)Application.Current!.Resources["Space8"];
            Root.ColumnDefinitions = new ColumnDefinitionCollection(new ColumnDefinition(GridLength.Star), new ColumnDefinition(GridLength.Star));
            MainColumn.WidthRequest = -1;
            MainColumn.HorizontalOptions = LayoutOptions.Fill;
            MainColumn.Margin = new Thickness(0);
            Grid.SetColumn(MainColumn, 1);
            if (!_twoPane)
            {
                ConnectedStack.Remove(Ring);
                Ring.VerticalOptions = LayoutOptions.Center;
                RingPane.Add(Ring);
            }
            RingPane.IsVisible = true;
            _twoPane = true;
            FitRing();
        }
        else
        {
            Root.Padding = new Thickness(0, 0, 0, 16);
            Root.ColumnDefinitions = new ColumnDefinitionCollection();
            Grid.SetColumn(MainColumn, 0);
            MainColumn.ApplyColumn(layout);
            if (_twoPane)
            {
                RingPane.Remove(Ring);
                Ring.VerticalOptions = LayoutOptions.Fill;
                ConnectedStack.Insert(1, Ring); // index 1 = right after GlovesSummaryCard
                RingPane.IsVisible = false;
                _twoPane = false;
            }
            Ring.Fit(layout.Width, null, layout.ColumnWidth);
        }
    }

    private void FitRing()
    {
        if (_twoPane)
            Ring.Fit(RingPane.Width, RingPane.Height, RingPane.Width);
    }

    private void UpdateProfileLayout()
    {
        var stacked = OsAccessibility.StackForLargeText;
        ProfileRow.ColumnDefinitions = stacked
            ? new ColumnDefinitionCollection(new ColumnDefinition(GridLength.Star))
            : new ColumnDefinitionCollection(new ColumnDefinition(GridLength.Star), new ColumnDefinition(GridLength.Auto));
        ProfileRow.RowDefinitions = stacked
            ? new RowDefinitionCollection(new RowDefinition(GridLength.Auto), new RowDefinition(GridLength.Auto))
            : new RowDefinitionCollection(new RowDefinition(GridLength.Auto));
        Grid.SetRow(ChangeLink, stacked ? 1 : 0);
        Grid.SetColumn(ChangeLink, stacked ? 0 : 1);
        ChangeLink.HorizontalOptions = stacked ? LayoutOptions.End : LayoutOptions.Fill;
    }

    // Visible acknowledgment for tremor, as on the Gloves summary; only when the tap does something.
    private async void OnProfileCardTapped(object? sender, TappedEventArgs e)
    {
        if (sender is not VisualElement card || !_viewModel.CanChangeProfile || Motion.Reduce)
            return;
        var fast = (uint)(int)Application.Current!.Resources["DurationFast"];
        await card.ScaleToAsync(0.98, fast, Motion.Standard);
        await card.ScaleToAsync(1.0, fast, Motion.Standard);
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        _viewModel.OnPageAppearing();
    }
}
