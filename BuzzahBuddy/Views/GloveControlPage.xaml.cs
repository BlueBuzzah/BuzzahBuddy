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

        // Subscribe to Unloaded event for cleanup
        Unloaded += OnPageUnloaded;
    }

    /// <summary>
    /// Handles RadioButton CheckedChanged event to invoke the ViewModel's SelectProfile command.
    /// This bridges the RadioButton's native behavior to the ViewModel's command pattern.
    /// </summary>
    private void OnProfileRadioButtonChecked(object? sender, CheckedChangedEventArgs e)
    {
        if (!e.Value) return; // Only handle when checked, not unchecked

        if (sender is RadioButton radioButton &&
            radioButton.BindingContext is ProfileItemViewModel profileItem)
        {
            _viewModel.SelectProfileCommand.Execute(profileItem);
        }
    }

    private void OnPageUnloaded(object? sender, EventArgs e)
    {
        // Dispose ViewModel to unsubscribe from events and stop timers
        _viewModel.Dispose();
    }
}
