using System.Windows.Input;
using BuzzahBuddy.Models;
using Microsoft.Maui.Controls.Internals;

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

    public static readonly BindableProperty FormattedMessageProperty = BindableProperty.Create(
        nameof(FormattedMessage), typeof(FormattedString), typeof(Banner), null, propertyChanged: Update);

    public static readonly BindableProperty DetailProperty = BindableProperty.Create(
        nameof(Detail), typeof(string), typeof(Banner), null, propertyChanged: Update);

    public static readonly BindableProperty ActionTextProperty = BindableProperty.Create(
        nameof(ActionText), typeof(string), typeof(Banner), null, propertyChanged: Update);

    public static readonly BindableProperty ActionCommandProperty = BindableProperty.Create(
        nameof(ActionCommand), typeof(ICommand), typeof(Banner), null, propertyChanged: Update);

    public static readonly BindableProperty ActionAtEndProperty = BindableProperty.Create(
        nameof(ActionAtEnd), typeof(bool), typeof(Banner), false, propertyChanged: Update);

    public static readonly BindableProperty AnnounceProperty = BindableProperty.Create(
        nameof(Announce), typeof(bool), typeof(Banner), false);

    public Banner()
    {
        InitializeComponent();
        StrutLabel.SizeChanged += (_, _) => AlignGlyph();
        GlyphLabel.SizeChanged += (_, _) => AlignGlyph();
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

    /// <summary>The message with inline styling (the legal notices' bold "Important:"); replaces Message.</summary>
    public FormattedString? FormattedMessage { get => (FormattedString?)GetValue(FormattedMessageProperty); set => SetValue(FormattedMessageProperty, value); }

    /// <summary>Technical detail (raw firmware or exception text), shown in code-s.</summary>
    public string? Detail { get => (string?)GetValue(DetailProperty); set => SetValue(DetailProperty, value); }

    public string? ActionText { get => (string?)GetValue(ActionTextProperty); set => SetValue(ActionTextProperty, value); }

    public ICommand? ActionCommand { get => (ICommand?)GetValue(ActionCommandProperty); set => SetValue(ActionCommandProperty, value); }

    /// <summary>Right-aligns the action (owner override for the auto-connect Try Again; the README's actions sit at the start).</summary>
    public bool ActionAtEnd { get => (bool)GetValue(ActionAtEndProperty); set => SetValue(ActionAtEndProperty, value); }

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

    private bool _announceQueued;

    // View models set visibility, title and message one after another, so speak once on the
    // next main-loop turn, when they have settled.
    private void AnnounceIfAsked()
    {
        if (!Announce || _announceQueued)
            return;
        _announceQueued = true;
        Dispatcher.Dispatch(() =>
        {
            _announceQueued = false;
            if (IsVisible && !string.IsNullOrEmpty(Title) && IsOnFrontPage())
                SemanticScreenReader.Announce(string.IsNullOrEmpty(Message) ? Title : $"{Title}. {Message}");
        });
    }

    // Tab pages are singletons that stay alive off screen; only the page in front speaks.
    private bool IsOnFrontPage()
    {
        Element? element = this;
        while (element is not null and not Page)
            element = element.Parent;
        return element is Page page
            && (page.Navigation.ModalStack.LastOrDefault() ?? Shell.Current?.CurrentPage) == page;
    }

    private void Apply()
    {
        var (ground, ink, glyph) = StatusToneStyle.For(Tone);
        Root.SetDynamicResource(BackgroundColorProperty, ground);
        GlyphLabel.SetDynamicResource(Label.TextColorProperty, ink);
        GlyphLabel.Text = glyph;

        TitleLabel.Text = Title;
        TitleLabel.IsVisible = !string.IsNullOrEmpty(Title);
        if (FormattedMessage is not null)
            MessageLabel.FormattedText = FormattedMessage;
        else
            MessageLabel.Text = Message;
        MessageLabel.IsVisible = FormattedMessage is not null || !string.IsNullOrEmpty(Message);
        DetailLabel.Text = Detail;
        DetailLabel.IsVisible = !string.IsNullOrEmpty(Detail);
        StrutLabel.Style = TitleLabel.IsVisible ? TitleLabel.Style : MessageLabel.Style;
        AlignGlyph();

        // Actions inside a banner follow its tone and are always outlined (Banner README).
        ActionButton.Text = ActionText;
        ActionButton.Command = ActionCommand;
        ActionButton.IsVisible = !string.IsNullOrEmpty(ActionText);
        ActionButton.HorizontalOptions = ActionAtEnd ? LayoutOptions.End : LayoutOptions.Start;
        if (ink != _actionInk)
        {
            // ButtonQuiet's states repaint Brand on every state change, Normal included, so
            // colors set directly here were lost; the banner restates the states in its ink.
            _actionInk = ink;
            VisualStateManager.SetVisualStateGroups(ActionButton, ActionStates(ink));
        }
        if (ActionText is not null)
            SemanticProperties.SetDescription(ActionButton, ActionText);
    }

    private string? _actionInk;

    /// <summary>ButtonQuiet's states in the tone's ink; Pressed dims instead of tinting the ground.</summary>
    private static VisualStateGroupList ActionStates(string ink)
    {
        static Setter Set(BindableProperty property, object value) => new() { Property = property, Value = value };
        VisualState State(string name, string text, string border, params Setter[] extra)
        {
            var state = new VisualState { Name = name };
            state.Setters.Add(Set(Button.TextColorProperty, new DynamicResource(text)));
            state.Setters.Add(Set(Button.BorderColorProperty, new DynamicResource(border)));
            foreach (var setter in extra)
                state.Setters.Add(setter);
            return state;
        }

        var group = new VisualStateGroup { Name = "CommonStates" };
        group.States.Add(State("Normal", ink, ink));
        group.States.Add(State("PointerOver", ink, ink));
        group.States.Add(State("Pressed", ink, ink, Set(OpacityProperty, 0.7)));
        group.States.Add(State("Focused", ink, "Focus", Set(Button.BorderWidthProperty, 3d)));
        group.States.Add(State("Disabled", "InkDisabled", "InkDisabled",
            Set(BackgroundColorProperty, new DynamicResource("FillDisabled"))));
        return [group];
    }

    // Centers the icon on the first line of text. The strut is that line's natural height;
    // LineHeight adds its extra space above each line on iOS and below it on Android.
    private void AlignGlyph()
    {
        var line = StrutLabel.Height;
        if (line <= 0 || GlyphLabel.Height <= 0)
            return;
        var first = TitleLabel.IsVisible ? TitleLabel : MessageLabel;
        var center = DeviceInfo.Platform == DevicePlatform.iOS ? line * first.LineHeight - line / 2 : line / 2;
        GlyphLabel.TranslationY = center - GlyphLabel.Height / 2;
    }
}
