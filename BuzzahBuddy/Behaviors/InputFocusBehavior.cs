using BuzzahBuddy.Helpers;

namespace BuzzahBuddy.Behaviors;

/// <summary>
/// Attaches to a <see cref="Border"/> wrapping an Entry/Editor/Picker and swaps the
/// border's stroke color between the default and focused states: default border #0A3143,
/// focused border #35B6F2 (the visible focus indicator every interactive control needs —
/// see DESIGN_GUIDE.md, "Accessibility"). Token values live in Resources/Styles/Colors.xaml.
/// </summary>
public class InputFocusBehavior : Behavior<Border>
{
    private VisualElement? _input;

    protected override void OnAttachedTo(Border bindable)
    {
        base.OnAttachedTo(bindable);

        if (bindable.Content is VisualElement input)
        {
            _input = input;
            _input.Focused += OnInputFocused;
            _input.Unfocused += OnInputUnfocused;
        }
    }

    protected override void OnDetachingFrom(Border bindable)
    {
        base.OnDetachingFrom(bindable);

        if (_input is not null)
        {
            _input.Focused -= OnInputFocused;
            _input.Unfocused -= OnInputUnfocused;
            _input = null;
        }
    }

    private void OnInputFocused(object? sender, FocusEventArgs e)
    {
        if (sender is VisualElement { Parent: Border border })
        {
            border.Stroke = new SolidColorBrush(ColorResources.Get("Primary", DesignColors.Primary));
        }
    }

    private void OnInputUnfocused(object? sender, FocusEventArgs e)
    {
        if (sender is VisualElement { Parent: Border border })
        {
            border.Stroke = new SolidColorBrush(ColorResources.Get("CardBorder", DesignColors.CardBorder));
        }
    }
}
