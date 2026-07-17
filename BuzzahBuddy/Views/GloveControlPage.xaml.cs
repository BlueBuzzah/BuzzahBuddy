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
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        _viewModel.OnPageAppearing();
    }
}
