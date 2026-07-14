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

    private async void OnProfileCardTapped(object? sender, TappedEventArgs e)
    {
        // Micro-feedback: tremor users need visible tap acknowledgment. Pure visual, no logic.
        if (sender is not VisualElement card)
            return;
        if (Helpers.Motion.Reduce)
            return;
        await card.ScaleTo(0.98, 100, Easing.CubicOut);
        await card.ScaleTo(1.0, 100, Easing.CubicOut);
    }
}
