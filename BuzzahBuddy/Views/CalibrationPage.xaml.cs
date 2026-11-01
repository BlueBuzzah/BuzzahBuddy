using System.ComponentModel;
using BuzzahBuddy.Helpers;
using BuzzahBuddy.Models;
using BuzzahBuddy.ViewModels;

namespace BuzzahBuddy.Views;

public partial class CalibrationPage : ContentPage
{
    private readonly CalibrationViewModel _viewModel;

    public CalibrationPage(CalibrationViewModel viewModel)
    {
        InitializeComponent();
        _viewModel = viewModel;
        BindingContext = viewModel;

        _viewModel.PropertyChanged += OnViewModelPropertyChanged;
        Unloaded += OnPageUnloaded;
        this.TrackLayout(ApplyLayout);
    }

    private bool? _twoPane;

    // Always one centered column. On a wide window the two gloves sit side by side inside it;
    // otherwise Primary sits above Secondary. The two sections are moved, never recreated, so
    // their bindings and AutomationIds stay intact; they are only re-arranged on a mode change.
    private void ApplyLayout(PageLayout layout)
    {
        Column.ApplyColumn(layout);

        if (_twoPane == layout.TwoPane)
            return;
        _twoPane = layout.TwoPane;

        // Row 1 is empty on two panes, so its spacing would be a stray gap.
        FingerGrid.RowSpacing = layout.TwoPane ? 0 : (double)Application.Current!.Resources["Space3"];
        FingerGrid.ColumnSpacing = (double)Application.Current!.Resources["Space6"];
        FingerGrid.ColumnDefinitions = layout.TwoPane
            ? new ColumnDefinitionCollection(new ColumnDefinition(GridLength.Star), new ColumnDefinition(GridLength.Star))
            : new ColumnDefinitionCollection();
        Grid.SetRow(SecondarySection, layout.TwoPane ? 0 : 1);
        Grid.SetColumn(SecondarySection, layout.TwoPane ? 1 : 0);
    }

    // Test mode lasts exactly as long as this page is on screen.
    protected override void OnAppearing()
    {
        base.OnAppearing();
        _viewModel.OpenAsync().SafeFireAndForget("[CALIBRATION]");
    }

    protected override void OnDisappearing()
    {
        base.OnDisappearing();
        _viewModel.CloseAsync().SafeFireAndForget("[CALIBRATION]");
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        // Move screen-reader focus to the step heading when the wizard advances
        if (e.PropertyName == nameof(CalibrationViewModel.CurrentStep))
            StepTitleLabel.SetSemanticFocus();
    }

    private void OnPageUnloaded(object? sender, EventArgs e)
    {
        _viewModel.PropertyChanged -= OnViewModelPropertyChanged;
        _viewModel.Dispose();
    }
}
