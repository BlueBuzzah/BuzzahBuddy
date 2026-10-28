using BuzzahBuddy.Helpers;

namespace BuzzahBuddy.Views.Controls;

/// <summary>Picks one of two to four fixed options, shown side by side.</summary>
public partial class SegmentedControl : ContentView
{
    public static readonly BindableProperty LabelProperty = BindableProperty.Create(
        nameof(Label), typeof(string), typeof(SegmentedControl), string.Empty, propertyChanged: Rebuild);

    public static readonly BindableProperty OptionsProperty = BindableProperty.Create(
        nameof(Options), typeof(IList<SegmentOption>), typeof(SegmentedControl), null, propertyChanged: Rebuild);

    public static readonly BindableProperty SelectedValueProperty = BindableProperty.Create(
        nameof(SelectedValue), typeof(int), typeof(SegmentedControl), 0, BindingMode.TwoWay, propertyChanged: Rebuild);

    public SegmentedControl()
    {
        InitializeComponent();
    }

    public string Label { get => (string)GetValue(LabelProperty); set => SetValue(LabelProperty, value); }

    public IList<SegmentOption>? Options { get => (IList<SegmentOption>?)GetValue(OptionsProperty); set => SetValue(OptionsProperty, value); }

    public int SelectedValue { get => (int)GetValue(SelectedValueProperty); set => SetValue(SelectedValueProperty, value); }

    private static void Rebuild(BindableObject bindable, object oldValue, object newValue) => ((SegmentedControl)bindable).Build();

    private void Build()
    {
        LabelText.Text = Label;
        LabelText.IsVisible = !string.IsNullOrEmpty(Label);
        Track.Children.Clear();
        Track.ColumnDefinitions.Clear();
        var options = Options ?? Array.Empty<SegmentOption>();
        for (var i = 0; i < options.Count; i++)
        {
            var option = options[i];
            var selected = option.Value == SelectedValue;
            var button = new Button
            {
                Text = option.Label,
                MinimumHeightRequest = (double)Application.Current!.Resources["Target"],
                CornerRadius = (int)Application.Current.Resources["RadiusMdInt"],
                Padding = new Thickness(12, 0),
                BorderWidth = 0,
                FontSize = 17,
                LineBreakMode = LineBreakMode.WordWrap,
                AutomationId = $"Segment{option.Value}",
            };
            button.SetDynamicResource(Button.FontFamilyProperty, selected ? "FontSans700" : "FontSans400");
            button.SetDynamicResource(BackgroundColorProperty, selected ? "Brand" : "SurfaceSunken");
            button.SetDynamicResource(Button.TextColorProperty, selected ? "OnBrand" : "Ink");
            if (selected)
            {
                var check = new FontImageSource { FontFamily = "Lucide", Glyph = LucideIcons.Check, Size = 20 };
                check.SetDynamicResource(FontImageSource.ColorProperty, "OnBrand");
                button.ImageSource = check;
                button.ContentLayout = new Button.ButtonContentLayout(Button.ButtonContentLayout.ImagePosition.Left, 6);
            }
            SemanticProperties.SetDescription(button, $"{option.Label}, {i + 1} of {options.Count}{(selected ? ", selected" : "")}");
            button.Clicked += (_, _) =>
            {
                if (SelectedValue != option.Value)
                    SelectedValue = option.Value;
            };
            Track.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Star));
            Track.Add(button, i, 0);
        }
    }
}
