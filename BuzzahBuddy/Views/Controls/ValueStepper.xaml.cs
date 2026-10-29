using BuzzahBuddy.Helpers;

namespace BuzzahBuddy.Views.Controls;

/// <summary>Sets a whole number between limits with large minus and plus buttons.</summary>
public partial class ValueStepper : ContentView
{
    public static readonly BindableProperty LabelProperty = BindableProperty.Create(
        nameof(Label), typeof(string), typeof(ValueStepper), string.Empty, propertyChanged: Update);

    public static readonly BindableProperty ValueProperty = BindableProperty.Create(
        nameof(Value), typeof(int), typeof(ValueStepper), 0, BindingMode.TwoWay, propertyChanged: Update);

    public static readonly BindableProperty MinimumProperty = BindableProperty.Create(
        nameof(Minimum), typeof(int), typeof(ValueStepper), 0, propertyChanged: Update);

    public static readonly BindableProperty MaximumProperty = BindableProperty.Create(
        nameof(Maximum), typeof(int), typeof(ValueStepper), 100, propertyChanged: Update);

    public static readonly BindableProperty StepProperty = BindableProperty.Create(
        nameof(Step), typeof(int), typeof(ValueStepper), 1);

    public static readonly BindableProperty UnitProperty = BindableProperty.Create(
        nameof(Unit), typeof(string), typeof(ValueStepper), null, propertyChanged: Update);

    public static readonly BindableProperty HintProperty = BindableProperty.Create(
        nameof(Hint), typeof(string), typeof(ValueStepper), null, propertyChanged: Update);

    public ValueStepper()
    {
        InitializeComponent();
        MinusButton.Text = LucideIcons.Minus;
        PlusButton.Text = LucideIcons.Plus;
        Apply();
    }

    public string Label { get => (string)GetValue(LabelProperty); set => SetValue(LabelProperty, value); }
    public int Value { get => (int)GetValue(ValueProperty); set => SetValue(ValueProperty, value); }
    public int Minimum { get => (int)GetValue(MinimumProperty); set => SetValue(MinimumProperty, value); }
    public int Maximum { get => (int)GetValue(MaximumProperty); set => SetValue(MaximumProperty, value); }
    public int Step { get => (int)GetValue(StepProperty); set => SetValue(StepProperty, value); }

    /// <summary>Spelled out after the number: "minutes", "%".</summary>
    public string? Unit { get => (string?)GetValue(UnitProperty); set => SetValue(UnitProperty, value); }

    public string? Hint { get => (string?)GetValue(HintProperty); set => SetValue(HintProperty, value); }

    private string Spoken => string.IsNullOrEmpty(Unit) ? $"{Label} {Value}" : $"{Label} {Value} {Unit}";

    private void OnMinusClicked(object? sender, EventArgs e) => Set(Value - Step);

    private void OnPlusClicked(object? sender, EventArgs e) => Set(Value + Step);

    private void Set(int value)
    {
        var clamped = Math.Clamp(value, Minimum, Maximum);
        if (clamped == Value)
            return;
        Value = clamped;
        SemanticScreenReader.Announce(Spoken);
    }

    private static void Update(BindableObject bindable, object oldValue, object newValue) => ((ValueStepper)bindable).Apply();

    private void Apply()
    {
        LabelText.Text = Label;
        NumberText.Text = Value.ToString(System.Globalization.CultureInfo.CurrentCulture);
        UnitText.Text = Unit;
        UnitText.IsVisible = !string.IsNullOrEmpty(Unit);
        HintText.Text = Hint;
        HintText.IsVisible = !string.IsNullOrEmpty(Hint);
        MinusButton.IsEnabled = Value > Minimum;
        PlusButton.IsEnabled = Value < Maximum;
        SemanticProperties.SetDescription(MinusButton, $"Decrease {Label}");
        SemanticProperties.SetDescription(PlusButton, $"Increase {Label}");
        SemanticProperties.SetDescription(Well, Spoken);
    }
}
