using BuzzahBuddy.Models;

namespace BuzzahBuddy.Views.Controls;

/// <summary>One condition in one to three words, always with an icon or dot so it never relies on color.</summary>
public partial class StatusBadge : ContentView
{
    public static readonly BindableProperty ToneProperty = BindableProperty.Create(
        nameof(Tone), typeof(StatusTone), typeof(StatusBadge), StatusTone.Neutral, propertyChanged: Update);

    public static readonly BindableProperty TextProperty = BindableProperty.Create(
        nameof(Text), typeof(string), typeof(StatusBadge), string.Empty, propertyChanged: Update);

    public static readonly BindableProperty GlyphProperty = BindableProperty.Create(
        nameof(Glyph), typeof(string), typeof(StatusBadge), null, propertyChanged: Update);

    public StatusBadge()
    {
        InitializeComponent();
        Apply();
    }

    public StatusTone Tone
    {
        get => (StatusTone)GetValue(ToneProperty);
        set => SetValue(ToneProperty, value);
    }

    public string Text
    {
        get => (string)GetValue(TextProperty);
        set => SetValue(TextProperty, value);
    }

    /// <summary>Overrides the tone's glyph (e.g. Bluetooth for "Connecting").</summary>
    public string? Glyph
    {
        get => (string?)GetValue(GlyphProperty);
        set => SetValue(GlyphProperty, value);
    }

    private static void Update(BindableObject bindable, object oldValue, object newValue) => ((StatusBadge)bindable).Apply();

    private void Apply()
    {
        var (ground, ink, toneGlyph) = StatusToneStyle.For(Tone);
        var glyph = Glyph ?? toneGlyph;
        Root.SetDynamicResource(BackgroundColorProperty, ground);
        Root.SetDynamicResource(Border.StrokeProperty, ink);
        GlyphLabel.SetDynamicResource(Label.TextColorProperty, ink);
        TextLabel.SetDynamicResource(Label.TextColorProperty, ink);
        Dot.SetDynamicResource(Microsoft.Maui.Controls.Shapes.Shape.FillProperty, ink);
        GlyphLabel.Text = glyph;
        GlyphLabel.IsVisible = glyph is not null;
        Dot.IsVisible = glyph is null;
        TextLabel.Text = Text;
        SemanticProperties.SetDescription(Root, Text);
    }
}
