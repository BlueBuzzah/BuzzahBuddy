using BuzzahBuddy.Helpers;
using BuzzahBuddy.Models;
using BuzzahBuddy.ViewModels;

namespace BuzzahBuddy.Views;

public partial class DeviceListPage : ContentPage
{
    private readonly DeviceListViewModel _viewModel;
    private PageLayout? _layout;

    public DeviceListPage(DeviceListViewModel viewModel)
    {
        InitializeComponent();
        _viewModel = viewModel;
        BindingContext = viewModel;
        this.TrackLayout(ApplyLayout);
        // Two panes only while connected; the page is a singleton and ConnectionInfo is get-only, so one subscription.
        viewModel.ConnectionInfo.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName is "IsConnected" or null or "" && _layout is { } l)
                ApplyLayout(l);
        };
    }

    // Connected on a wide window: two panes (header, options, apply and disconnect | profile). The six profile
    // cards alone fill the right pane on an 11" window, so Apply sits left where it stays on screen.
    // Otherwise one centered column. Each section is one instance moved between the stacks, so its
    // bindings, AutomationIds and the profile list's x:Reference bindings stay intact.
    private void ApplyLayout(PageLayout layout)
    {
        _layout = layout;
        var twoPane = layout.TwoPane && _viewModel.ConnectionInfo.IsConnected;

        LeftStack.Clear();
        RightStack.Clear();
        if (twoPane)
        {
            ConnectedGrid.WidthRequest = -1;
            ConnectedGrid.HorizontalOptions = LayoutOptions.Fill;
            ConnectedGrid.Margin = new Thickness(layout.Gutter, 0);
            ConnectedGrid.ColumnSpacing = (double)Application.Current!.Resources["Space8"];
            ConnectedGrid.ColumnDefinitions = new ColumnDefinitionCollection(new ColumnDefinition(GridLength.Star), new ColumnDefinition(GridLength.Star));
            LeftStack.Add(HeaderSection);
            LeftStack.Add(OptionsSection);
            LeftStack.Add(ApplySection);
            LeftStack.Add(DisconnectSection);
            RightStack.Add(ProfileSection);
            RightStack.IsVisible = true;
        }
        else
        {
            ConnectedGrid.Margin = new Thickness(0);
            ConnectedGrid.ApplyColumn(layout);
            ConnectedGrid.ColumnSpacing = 0;
            ConnectedGrid.ColumnDefinitions = new ColumnDefinitionCollection(new ColumnDefinition(GridLength.Star));
            LeftStack.Add(HeaderSection);
            LeftStack.Add(ProfileSection);
            LeftStack.Add(OptionsSection);
            LeftStack.Add(ApplySection);
            LeftStack.Add(DisconnectSection);
            RightStack.IsVisible = false;
        }

        ScanGrid.ApplyColumn(layout);
        ScanButton.ApplyColumn(layout);
        // The overlay's card: the old 32 side margins on a phone, capped at 560 on wide windows.
        OverlayCard.WidthRequest = Math.Max(0, Math.Min(560, layout.Width - 64));
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
