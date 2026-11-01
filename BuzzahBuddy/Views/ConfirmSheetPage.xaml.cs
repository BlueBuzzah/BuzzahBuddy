using BuzzahBuddy.Helpers;
using BuzzahBuddy.Services.Dialogs;
using Microsoft.Maui.Controls.Shapes;
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

        // iOS: let the sheet's background run under the home indicator (the scrim would
        // otherwise show as a strip below it); LayoutSheet pads the buttons above it.
        if (DeviceInfo.Platform == DevicePlatform.iOS)
        {
            var edges = new SafeAreaEdges(SafeAreaRegions.Container, SafeAreaRegions.Container, SafeAreaRegions.Container, SafeAreaRegions.None);
            SafeAreaEdges = edges;
            Root.SafeAreaEdges = edges;
        }
        SizeChanged += (_, _) => LayoutSheet();

        // Screen readers start on the title. Not in OnAppearing: the label has no handler yet.
        Loaded += (_, _) => TitleLabel.SetSemanticFocus();
        // The page is pushed without animation so the scrim shows at once; only the sheet slides.
        Sheet.SizeChanged += SlideIn;
    }

    /// <summary>True only when the confirm button was tapped.</summary>
    public Task<bool> Result => _result.Task;

    /// <summary>Slides the sheet back down; the caller then removes the page.</summary>
    public Task SlideOutAsync() =>
        Motion.Reduce ? Task.CompletedTask : Sheet.TranslateToAsync(0, Sheet.Height, Duration("DurationBase"), Motion.Standard);

    private const double WideWindow = 600, SheetMaxWidth = 560, FloatingMargin = 24, SheetCorner = 28;

    /// <summary>
    /// Narrow windows: a full-width bottom sheet. From 600 wide: a centered card, at most 560
    /// wide, floating 24 above the bottom with all corners round. Re-runs on every size
    /// change so rotating with the sheet open re-centers it.
    /// </summary>
    private void LayoutSheet()
    {
        if (Width <= 0)
            return;
        var pad = (double)Microsoft.Maui.Controls.Application.Current!.Resources["Space8"];
        var inset = DeviceInfo.Platform == DevicePlatform.iOS ? On<iOS>().SafeAreaInsets().Bottom : 0;
        if (Width >= WideWindow)
        {
            Sheet.MaximumWidthRequest = SheetMaxWidth;
            Sheet.WidthRequest = Math.Min(SheetMaxWidth, Width);
            Sheet.HorizontalOptions = LayoutOptions.Center;
            Sheet.Margin = new Thickness(0, 0, 0, Math.Max(FloatingMargin, inset));
            Sheet.Padding = new Thickness(pad);
            Sheet.StrokeShape = new RoundRectangle { CornerRadius = new CornerRadius(SheetCorner) };
        }
        else
        {
            Sheet.MaximumWidthRequest = double.PositiveInfinity;
            Sheet.WidthRequest = -1;
            Sheet.HorizontalOptions = LayoutOptions.Fill;
            Sheet.Margin = new Thickness(0);
            Sheet.Padding = new Thickness(pad, pad, pad, pad + inset);
            Sheet.StrokeShape = new RoundRectangle { CornerRadius = new CornerRadius(SheetCorner, SheetCorner, 0, 0) };
        }
        // The text scrolls only when the buttons would not fit. A star row filled the whole
        // window on Android, so the row is Auto and its height is capped here instead.
        MessageScroll.MaximumHeightRequest = Math.Max(96,
            Height - Root.Padding.Top - Sheet.Margin.Bottom - Sheet.Padding.VerticalThickness - ButtonsHeight);
    }

    // Confirm (56) and Cancel (56) plus the 12 and 20 above them.
    private const double ButtonsHeight = 144;

    private void SlideIn(object? sender, EventArgs e)
    {
        if (Sheet.Height <= 0)
            return;
        Sheet.SizeChanged -= SlideIn;
        if (Motion.Reduce)
            return;
        Sheet.TranslationY = Sheet.Height;
        _ = Sheet.TranslateToAsync(0, 0, Duration("DurationSlow"), Motion.Standard);
    }

    private static uint Duration(string key) => (uint)(int)Microsoft.Maui.Controls.Application.Current!.Resources[key];

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
