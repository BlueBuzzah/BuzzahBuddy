using BuzzahBuddy.ViewModels;

namespace BuzzahBuddy;

public partial class MainPage : ContentPage
{
    private readonly MainPageViewModel _viewModel;

    public MainPage(MainPageViewModel viewModel)
    {
        InitializeComponent();
        _viewModel = viewModel;
        BindingContext = viewModel;

        // Subscribe to Unloaded event for cleanup
        Unloaded += OnPageUnloaded;
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        // Refresh connection state when page becomes visible
        _viewModel.RefreshConnectionState();
    }

    private void OnPageUnloaded(object? sender, EventArgs e)
    {
        // Dispose ViewModel to unsubscribe from events
        _viewModel.Dispose();
    }
}
