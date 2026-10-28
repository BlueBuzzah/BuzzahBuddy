using BuzzahBuddy.Helpers;

namespace BuzzahBuddy.Views.Controls;

/// <summary>An on/off setting that applies at once; the whole row toggles it.</summary>
public partial class SwitchRow : ContentView
{
    public static readonly BindableProperty LabelProperty = BindableProperty.Create(
        nameof(Label), typeof(string), typeof(SwitchRow), string.Empty, propertyChanged: Update);

    public static readonly BindableProperty DescriptionProperty = BindableProperty.Create(
        nameof(Description), typeof(string), typeof(SwitchRow), null, propertyChanged: Update);

    public static readonly BindableProperty IsToggledProperty = BindableProperty.Create(
        nameof(IsToggled), typeof(bool), typeof(SwitchRow), false, BindingMode.TwoWay, propertyChanged: OnToggledChanged);

    public SwitchRow()
    {
        InitializeComponent();
        CheckGlyph.Text = LucideIcons.Check;
        Apply(animate: false);
        PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(IsEnabled))
                Apply(animate: false);
        };
    }

    public string Label { get => (string)GetValue(LabelProperty); set => SetValue(LabelProperty, value); }

    /// <summary>One line under the label, in body-s ink-muted.</summary>
    public string? Description { get => (string?)GetValue(DescriptionProperty); set => SetValue(DescriptionProperty, value); }

    public bool IsToggled { get => (bool)GetValue(IsToggledProperty); set => SetValue(IsToggledProperty, value); }

    private void OnTapped(object? sender, TappedEventArgs e)
    {
        if (!IsEnabled)
            return;
        IsToggled = !IsToggled;
        SemanticScreenReader.Announce(IsToggled ? "On" : "Off");
    }

    private static void Update(BindableObject bindable, object oldValue, object newValue) => ((SwitchRow)bindable).Apply(animate: false);

    private static void OnToggledChanged(BindableObject bindable, object oldValue, object newValue) => ((SwitchRow)bindable).Apply(animate: true);

    private void Apply(bool animate)
    {
        LabelText.Text = Label;
        DescriptionText.Text = Description;
        DescriptionText.IsVisible = !string.IsNullOrEmpty(Description);
        SemanticProperties.SetDescription(Root, $"{Label}, switch, {(IsToggled ? "on" : "off")}");
        SemanticProperties.SetHint(Root, Description);

        if (!IsEnabled)
        {
            Track.SetDynamicResource(BackgroundColorProperty, "FillDisabled");
            Track.SetDynamicResource(Border.StrokeProperty, "FillDisabled");
            LabelText.SetDynamicResource(Microsoft.Maui.Controls.Label.TextColorProperty, "InkDisabled");
        }
        else
        {
            Track.SetDynamicResource(BackgroundColorProperty, IsToggled ? "Brand" : "SurfaceSunken");
            Track.SetDynamicResource(Border.StrokeProperty, IsToggled ? "Brand" : "LineStrong");
            LabelText.SetDynamicResource(Microsoft.Maui.Controls.Label.TextColorProperty, "Ink");
        }
        Thumb.SetDynamicResource(BackgroundColorProperty, IsToggled ? "OnBrand" : "LineStrong");
        CheckGlyph.IsVisible = IsToggled;

        var x = IsToggled ? 24 : 0;
        if (animate && !Motion.Reduce)
            _ = Thumb.TranslateToAsync(x, 0, (uint)(int)Application.Current!.Resources["DurationFast"], Motion.Standard);
        else
            Thumb.TranslationX = x;
    }
}
