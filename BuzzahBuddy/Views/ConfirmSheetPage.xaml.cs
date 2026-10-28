using BuzzahBuddy.Services.Dialogs;
using Microsoft.Maui.Controls.PlatformConfiguration;
using Microsoft.Maui.Controls.PlatformConfiguration.iOSSpecific;

namespace BuzzahBuddy.Views;

/// <summary>The sheet behind <see cref="IConfirmationService"/>; see the XAML for the layout rules.</summary>
public partial class ConfirmSheetPage : ContentPage
{
    private readonly TaskCompletionSource<bool> _result = new();

    public ConfirmSheetPage(ConfirmRequest request)
    {
        InitializeComponent();
        // Keep the page behind visible through the scrim instead of iOS's default card.
        On<iOS>().SetModalPresentationStyle(UIModalPresentationStyle.OverFullScreen);

        TitleLabel.Text = request.Title;
        MessageLabel.Text = request.Message;
        ConfirmButton.Text = request.ConfirmText;
        ConfirmButton.Style = (Style)Microsoft.Maui.Controls.Application.Current!.Resources[request.Destructive ? "ButtonDanger" : "ButtonPrimary"];
        SemanticProperties.SetDescription(ConfirmButton, request.ConfirmText);
        CancelButton.Text = request.CancelText;
        SemanticProperties.SetDescription(CancelButton, request.CancelText);

        // Screen readers start on the title. Not in OnAppearing: the label has no handler yet.
        Loaded += (_, _) => TitleLabel.SetSemanticFocus();
    }

    /// <summary>True only when the confirm button was tapped.</summary>
    public Task<bool> Result => _result.Task;

    protected override void OnDisappearing()
    {
        base.OnDisappearing();
        _result.TrySetResult(false);
    }

    protected override bool OnBackButtonPressed()
    {
        _result.TrySetResult(false);
        return true; // ConfirmationService closes the sheet
    }

    private void OnConfirmClicked(object? sender, EventArgs e) => _result.TrySetResult(true);

    private void OnCancelClicked(object? sender, EventArgs e) => _result.TrySetResult(false);
}
