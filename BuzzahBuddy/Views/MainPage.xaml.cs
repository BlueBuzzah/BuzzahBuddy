using BuzzahBuddy.ViewModels;

namespace BuzzahBuddy.Views;

public partial class MainPage : ContentPage
{
    private readonly MainPageViewModel _viewModel;

    public MainPage(MainPageViewModel viewModel)
    {
        InitializeComponent();
        _viewModel = viewModel;
        BindingContext = viewModel;
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        // Refresh connection state when page becomes visible
        _viewModel.RefreshConnectionState();
    }

}
