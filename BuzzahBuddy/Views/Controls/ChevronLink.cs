using BuzzahBuddy.Helpers;

namespace BuzzahBuddy.Views.Controls;

/// <summary>
/// A tappable card's action as text and a chevron ("Change ›"): brand blue, or gray while
/// disabled. The card is the tap target; this only says what a tap does.
/// </summary>
public class ChevronLink : HorizontalStackLayout
{
    public static readonly BindableProperty TextProperty = BindableProperty.Create(
        nameof(Text), typeof(string), typeof(ChevronLink), null,
        propertyChanged: (b, _, v) => ((ChevronLink)b)._text.Text = (string?)v);

    private readonly Label _text = new() { VerticalOptions = LayoutOptions.Center };
    private readonly Label _chevron = new() { Text = LucideIcons.ChevronRight, VerticalOptions = LayoutOptions.Center };

    public ChevronLink()
    {
        Spacing = 4;
        VerticalOptions = LayoutOptions.Center;
        _text.SetDynamicResource(StyleProperty, "TypeLabel");
        _chevron.SetDynamicResource(StyleProperty, "IconLabel");
        _chevron.FontSize = 20;
        Children.Add(_text);
        Children.Add(_chevron);
        UpdateInk();
    }

    public string? Text { get => (string?)GetValue(TextProperty); set => SetValue(TextProperty, value); }

    protected override void OnPropertyChanged(string? propertyName = null)
    {
        base.OnPropertyChanged(propertyName);
        if (propertyName == nameof(IsEnabled))
            UpdateInk();
    }

    // Dynamic resources set in code, not a trigger: a trigger restores a fixed color when it
    // turns off and misses in-app theme switches.
    private void UpdateInk()
    {
        var ink = IsEnabled ? "Brand" : "InkDisabled";
        _text.SetDynamicResource(Label.TextColorProperty, ink);
        _chevron.SetDynamicResource(Label.TextColorProperty, ink);
    }
}
