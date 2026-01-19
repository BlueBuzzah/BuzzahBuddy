using BuzzahBuddy.ViewModels;

namespace BuzzahBuddy.Views;

public partial class SettingsPage : ContentPage
{
    private readonly SettingsViewModel _viewModel;

    public SettingsPage(SettingsViewModel viewModel)
    {
        InitializeComponent();
        _viewModel = viewModel;
        BindingContext = viewModel;

        // Subscribe to Unloaded event for cleanup
        Unloaded += OnPageUnloaded;
    }

    private void OnPageUnloaded(object? sender, EventArgs e)
    {
        // Dispose ViewModel to unsubscribe from events
        _viewModel.Dispose();
    }
}
