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
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        _viewModel.OnPageAppearing();
    }

    private async void OnCardTapped(object? sender, TappedEventArgs e)
    {
        // Micro-feedback: tremor users need visible tap acknowledgment. Pure visual, no logic.
        if (sender is not VisualElement card)
            return;
        if (Helpers.Motion.Reduce)
            return;
        // duration-fast, ease-standard (design system Motion).
        var fast = (uint)(int)Application.Current!.Resources["DurationFast"];
        await card.ScaleTo(0.98, fast, Helpers.Motion.Standard);
        await card.ScaleTo(1.0, fast, Helpers.Motion.Standard);
    }
}
