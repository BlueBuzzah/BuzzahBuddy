using BuzzahBuddy.Models;
using BuzzahBuddy.ViewModels;

namespace BuzzahBuddy.Views;

public partial class DeviceListPage : ContentPage
{
    private readonly DeviceListViewModel _viewModel;

    public DeviceListPage(DeviceListViewModel viewModel)
    {
        InitializeComponent();
        _viewModel = viewModel;
        BindingContext = viewModel;

        // Subscribe to Unloaded event for cleanup
        Unloaded += OnPageUnloaded;
    }

    private async void OnConnectClicked(object sender, EventArgs e)
    {
        System.Diagnostics.Debug.WriteLine("Connect button CLICKED!");

        if (sender is Button button && button.BindingContext is GloveDevice device)
        {
            System.Diagnostics.Debug.WriteLine($"Device from button: {device.Name} ({device.Id})");
            await _viewModel.ConnectCommand.ExecuteAsync(device);
        }
        else
        {
            System.Diagnostics.Debug.WriteLine($"Button binding context is: {(sender as Button)?.BindingContext?.GetType().Name ?? "null"}");
        }
    }

    private void OnPageUnloaded(object? sender, EventArgs e)
    {
        // Dispose ViewModel to unsubscribe from events
        _viewModel.Dispose();
    }
}
