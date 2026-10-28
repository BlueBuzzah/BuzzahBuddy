using System.Windows.Input;
using BuzzahBuddy.Models;

namespace BuzzahBuddy.Views.Controls;

/// <summary>A sentence-length message with a tone, icon, title, body and optional action.</summary>
public partial class Banner : ContentView
{
    public static readonly BindableProperty ToneProperty = BindableProperty.Create(
        nameof(Tone), typeof(StatusTone), typeof(Banner), StatusTone.Info, propertyChanged: Update);

    public static readonly BindableProperty TitleProperty = BindableProperty.Create(
        nameof(Title), typeof(string), typeof(Banner), null, propertyChanged: OnTitleChanged);

    public static readonly BindableProperty MessageProperty = BindableProperty.Create(
        nameof(Message), typeof(string), typeof(Banner), null, propertyChanged: Update);

    public static readonly BindableProperty DetailProperty = BindableProperty.Create(
        nameof(Detail), typeof(string), typeof(Banner), null, propertyChanged: Update);

    public static readonly BindableProperty ActionTextProperty = BindableProperty.Create(
        nameof(ActionText), typeof(string), typeof(Banner), null, propertyChanged: Update);

    public static readonly BindableProperty ActionCommandProperty = BindableProperty.Create(
        nameof(ActionCommand), typeof(ICommand), typeof(Banner), null, propertyChanged: Update);

    public static readonly BindableProperty AnnounceProperty = BindableProperty.Create(
        nameof(Announce), typeof(bool), typeof(Banner), false);

    public Banner()
    {
        InitializeComponent();
        Apply();
        PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(IsVisible) && IsVisible)
                AnnounceIfAsked();
        };
    }

    public StatusTone Tone { get => (StatusTone)GetValue(ToneProperty); set => SetValue(ToneProperty, value); }

    /// <summary>What happened.</summary>
    public string? Title { get => (string?)GetValue(TitleProperty); set => SetValue(TitleProperty, value); }

    /// <summary>What to do next.</summary>
    public string? Message { get => (string?)GetValue(MessageProperty); set => SetValue(MessageProperty, value); }

    /// <summary>Technical detail (raw firmware or exception text), shown in code-s.</summary>
    public string? Detail { get => (string?)GetValue(DetailProperty); set => SetValue(DetailProperty, value); }

    public string? ActionText { get => (string?)GetValue(ActionTextProperty); set => SetValue(ActionTextProperty, value); }

    public ICommand? ActionCommand { get => (ICommand?)GetValue(ActionCommandProperty); set => SetValue(ActionCommandProperty, value); }

    /// <summary>Speak the banner when it appears or its title changes (status changes).</summary>
    public bool Announce { get => (bool)GetValue(AnnounceProperty); set => SetValue(AnnounceProperty, value); }

    private static void Update(BindableObject bindable, object oldValue, object newValue) => ((Banner)bindable).Apply();

    private static void OnTitleChanged(BindableObject bindable, object oldValue, object newValue)
    {
        var banner = (Banner)bindable;
        banner.Apply();
        if (banner.IsVisible)
            banner.AnnounceIfAsked();
    }

    private void AnnounceIfAsked()
    {
        if (Announce && !string.IsNullOrEmpty(Title))
            SemanticScreenReader.Announce(string.IsNullOrEmpty(Message) ? Title : $"{Title}. {Message}");
    }

    private void Apply()
    {
        var (ground, ink, glyph) = StatusToneStyle.For(Tone);
        Root.SetDynamicResource(BackgroundColorProperty, ground);
        GlyphLabel.SetDynamicResource(Label.TextColorProperty, ink);
        GlyphLabel.Text = glyph;

        TitleLabel.Text = Title;
        TitleLabel.IsVisible = !string.IsNullOrEmpty(Title);
        MessageLabel.Text = Message;
        MessageLabel.IsVisible = !string.IsNullOrEmpty(Message);
        DetailLabel.Text = Detail;
        DetailLabel.IsVisible = !string.IsNullOrEmpty(Detail);

        // Actions inside a banner follow its tone and are always outlined (Banner README).
        ActionButton.Text = ActionText;
        ActionButton.Command = ActionCommand;
        ActionButton.IsVisible = !string.IsNullOrEmpty(ActionText);
        ActionButton.SetDynamicResource(Button.TextColorProperty, ink);
        ActionButton.SetDynamicResource(Button.BorderColorProperty, ink);
        if (ActionText is not null)
            SemanticProperties.SetDescription(ActionButton, ActionText);
    }
}
