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
        Grid.SetRow(ChangeButton, stacked ? 1 : 0);
        Grid.SetColumn(ChangeButton, stacked ? 0 : 1);
        ChangeButton.HorizontalOptions = stacked ? LayoutOptions.End : LayoutOptions.Fill;
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        _viewModel.OnPageAppearing();
    }
}
