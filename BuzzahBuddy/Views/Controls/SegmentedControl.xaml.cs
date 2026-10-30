using BuzzahBuddy.Helpers;

namespace BuzzahBuddy.Views.Controls;

/// <summary>Picks one of two to four fixed options, shown side by side (stacked at large text).</summary>
public partial class SegmentedControl : ContentView
{
    public static readonly BindableProperty LabelProperty = BindableProperty.Create(
        nameof(Label), typeof(string), typeof(SegmentedControl), string.Empty, propertyChanged: Rebuild);

    public static readonly BindableProperty OptionsProperty = BindableProperty.Create(
        nameof(Options), typeof(IList<SegmentOption>), typeof(SegmentedControl), null, propertyChanged: Rebuild);

    public static readonly BindableProperty SelectedValueProperty = BindableProperty.Create(
        nameof(SelectedValue), typeof(int), typeof(SegmentedControl), 0, BindingMode.TwoWay, propertyChanged: Restyle);

    private readonly List<(Button Button, SegmentOption Option)> _buttons = [];

    // At large text even "System" won't fit a third of the width, so options stack rather than
    // wrap. They also stack below that text size whenever a label doesn't fit its share (FitLabels).
    private bool _stacked;

    public SegmentedControl()
    {
        InitializeComponent();
        Track.SizeChanged += (_, _) => FitLabels();
        this.TrackTextScale(Arrange);
    }

    public string Label { get => (string)GetValue(LabelProperty); set => SetValue(LabelProperty, value); }

    public IList<SegmentOption>? Options { get => (IList<SegmentOption>?)GetValue(OptionsProperty); set => SetValue(OptionsProperty, value); }

    public int SelectedValue { get => (int)GetValue(SelectedValueProperty); set => SetValue(SelectedValueProperty, value); }

    private static void Rebuild(BindableObject bindable, object oldValue, object newValue) => ((SegmentedControl)bindable).Build();

    // A selection restyles the tapped button in place so it keeps focus.
    private static void Restyle(BindableObject bindable, object oldValue, object newValue) => ((SegmentedControl)bindable).ApplySelection();

    private void Build()
    {
        LabelText.Text = Label;
        LabelText.IsVisible = !string.IsNullOrEmpty(Label);
        Track.Children.Clear();
        _buttons.Clear();
        var options = Options ?? Array.Empty<SegmentOption>();
        for (var i = 0; i < options.Count; i++)
            _buttons.Add((CreateButton(options[i], i), options[i]));
        Arrange();
        ApplySelection();
    }

    private void Arrange()
    {
        _stacked = OsAccessibility.StackForLargeText;
        Lay();
    }

    private void Lay()
    {
        Track.ColumnDefinitions.Clear();
        Track.RowDefinitions.Clear();
        for (var i = 0; i < _buttons.Count; i++)
        {
            if (_stacked)
                Track.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
            else
                Track.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Star));
            Place(_buttons[i].Button, i);
        }
    }

    private void Place(Button button, int index)
    {
        Grid.SetColumn(button, _stacked ? 0 : index);
        Grid.SetRow(button, _stacked ? index : 0);
    }

    private Button CreateButton(SegmentOption option, int index)
    {
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
        button.Clicked += (_, _) =>
        {
            if (SelectedValue != option.Value)
                SelectedValue = option.Value;
        };
        Track.Add(button);
        Place(button, index);
        return button;
    }

    private void ApplySelection()
    {
        for (var i = 0; i < _buttons.Count; i++)
        {
            var (button, option) = _buttons[i];
            var selected = option.Value == SelectedValue;
            button.SetDynamicResource(Button.FontFamilyProperty, selected ? "FontSans700" : "FontSans400");
            button.SetDynamicResource(BackgroundColorProperty, selected ? "Brand" : "SurfaceSunken");
            button.SetDynamicResource(Button.TextColorProperty, selected ? "OnBrand" : "Ink");
            SemanticProperties.SetDescription(button, $"{option.Label}, {i + 1} of {_buttons.Count}{(selected ? ", selected" : "")}");
        }
        Dispatcher.Dispatch(FitLabels);
    }

    // Equal columns can't widen like the bundle's flex options. When a label doesn't fit its
    // share on one line, the options stack: a wrapped label clips, since the button doesn't
    // grow for it. Stacking holds until the text size is checked again.
    private bool _fitting;

    private void FitLabels()
    {
        if (_fitting || Track.Width <= 0 || _buttons.Count == 0)
            return;
        _fitting = true;
        var share = _stacked
            ? Track.Width
            : (Track.Width - Track.ColumnSpacing * (_buttons.Count - 1)) / _buttons.Count;
        var overflow = false;
        foreach (var (button, _) in _buttons)
        {
            overflow |= NaturalWidth(button) > share;
        }
        if (overflow && !_stacked)
        {
            _stacked = true;
            Lay();
        }
        _fitting = false;
    }

    private static double NaturalWidth(Button button) =>
        button.Measure(double.PositiveInfinity, double.PositiveInfinity).Width;
}
