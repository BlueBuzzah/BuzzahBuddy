using BuzzahBuddy.Helpers;

namespace BuzzahBuddy.Behaviors;

/// <summary>
/// Attaches to a <c>TextFieldBorder</c> wrapping an Entry/Editor and draws its edge:
/// a 3px <c>Focus</c> ring while focused (the visible focus indicator every control
/// needs, design system "Focus and states"), a 2px <c>Danger</c> edge while
/// <see cref="HasError"/>, otherwise the 2px <c>LineStrong</c> control edge.
/// </summary>
public class InputFocusBehavior : Behavior<Border>
{
    public static readonly BindableProperty HasErrorProperty = BindableProperty.Create(
        nameof(HasError), typeof(bool), typeof(InputFocusBehavior), false,
        propertyChanged: (b, _, _) => ((InputFocusBehavior)b).UpdateStroke());

    /// <summary>True while the field's value is invalid.</summary>
    public bool HasError
    {
        get => (bool)GetValue(HasErrorProperty);
        set => SetValue(HasErrorProperty, value);
    }

    private Border? _border;
    private VisualElement? _input;

    protected override void OnAttachedTo(Border bindable)
    {
        base.OnAttachedTo(bindable);
        _border = bindable;
        // Behaviors don't inherit the BindingContext; follow the border's so HasError can bind.
        BindingContext = bindable.BindingContext;
        bindable.BindingContextChanged += OnBorderBindingContextChanged;

        if (bindable.Content is VisualElement input)
        {
            _input = input;
            _input.Focused += OnInputFocusChanged;
            _input.Unfocused += OnInputFocusChanged;
        }
        UpdateStroke();
    }

    protected override void OnDetachingFrom(Border bindable)
    {
        base.OnDetachingFrom(bindable);
        bindable.BindingContextChanged -= OnBorderBindingContextChanged;

        if (_input is not null)
        {
            _input.Focused -= OnInputFocusChanged;
            _input.Unfocused -= OnInputFocusChanged;
            _input = null;
        }
        _border = null;
    }

    private void OnBorderBindingContextChanged(object? sender, EventArgs e) =>
        BindingContext = _border?.BindingContext;

    private void OnInputFocusChanged(object? sender, FocusEventArgs e) => UpdateStroke();

    private void UpdateStroke()
    {
        if (_border is null)
            return;
        var (key, width) = _input?.IsFocused == true ? ("Focus", 3.0)
            : HasError ? ("Danger", 2.0)
            : ("LineStrong", 2.0);
        _border.SetDynamicResource(Border.StrokeProperty, key);
        _border.StrokeThickness = width;
    }
}
