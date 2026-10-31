using BuzzahBuddy.Helpers;
using BuzzahBuddy.ViewModels;

namespace BuzzahBuddy.Views;

public partial class GloveControlPage : ContentPage
{
    private readonly GloveControlViewModel _viewModel;

    public GloveControlPage(GloveControlViewModel viewModel)
    {
        InitializeComponent();
        _viewModel = viewModel;
        BindingContext = viewModel;
        ProfileRow.TrackTextScale(UpdateProfileLayout);
        // The content fills the viewport at least, so its star row (Get Started) can center.
        Scroller.SizeChanged += (_, _) => ScrollContent.MinimumHeightRequest = Scroller.Height;
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
