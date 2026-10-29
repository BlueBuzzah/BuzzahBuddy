using BuzzahBuddy.Helpers;

namespace BuzzahBuddy.Views.Controls;

/// <summary>
/// An on/off setting that applies at once; the whole row toggles it. The track is drawn, not
/// a native switch, so the row carries the platform's switch role, state and activation itself
/// (screen readers, Switch Control and keyboards).
/// </summary>
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
        Root.HandlerChanged += (_, _) =>
        {
#if ANDROID
            if (Root.Handler?.PlatformView is Android.Views.View view)
            {
                view.Focusable = true;
                view.KeyPress += OnKeyPress;
            }
#endif
            ApplyPlatformSwitch();
        };
    }

    public string Label { get => (string)GetValue(LabelProperty); set => SetValue(LabelProperty, value); }

    /// <summary>One line under the label, in body-s ink-muted.</summary>
    public string? Description { get => (string?)GetValue(DescriptionProperty); set => SetValue(DescriptionProperty, value); }

    public bool IsToggled { get => (bool)GetValue(IsToggledProperty); set => SetValue(IsToggledProperty, value); }

    private void OnTapped(object? sender, TappedEventArgs e) => Toggle();

    private void Toggle()
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
        // The platform role reads "switch" and the state; the description is just the name.
        SemanticProperties.SetDescription(Root, Label);
        SemanticProperties.SetHint(Root, Description);
        ApplyPlatformSwitch();

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

    /// <summary>Re-applied after each update: MAUI resets the native semantics when the description changes.</summary>
    private void ApplyPlatformSwitch()
    {
#if IOS || MACCATALYST
        if (Root.Handler?.PlatformView is UIKit.UIView view)
        {
            // VoiceOver, Switch Control and Full Keyboard Access activate it with a tap at its
            // centre, which the row's tap gesture handles.
            view.IsAccessibilityElement = true;
            var toggle = ToggleButtonTrait is not null;
            view.AccessibilityTraits = (ToggleButtonTrait ?? UIKit.UIAccessibilityTrait.Button)
                | (IsEnabled ? UIKit.UIAccessibilityTrait.None : UIKit.UIAccessibilityTrait.NotEnabled);
            view.AccessibilityValue = toggle ? (IsToggled ? "1" : "0") : (IsToggled ? "On" : "Off");
        }
#elif ANDROID
        if (Root.Handler?.PlatformView is Android.Views.View view &&
            AndroidX.Core.View.ViewCompat.GetAccessibilityDelegate(view) is not SwitchDelegate)
        {
            AndroidX.Core.View.ViewCompat.SetAccessibilityDelegate(view,
                new SwitchDelegate(this, AndroidX.Core.View.ViewCompat.GetAccessibilityDelegate(view)));
        }
#endif
    }

#if IOS || MACCATALYST
    /// <summary>
    /// iOS 17's toggle-button trait (VoiceOver: "switch button, on"). The .NET binding has no
    /// enum member for it, so it's read from UIKit; null before iOS 17, which falls back to Button.
    /// </summary>
    private static readonly UIKit.UIAccessibilityTrait? ToggleButtonTrait =
        OperatingSystem.IsIOSVersionAtLeast(17)
            ? (UIKit.UIAccessibilityTrait)ObjCRuntime.Dlfcn.GetUInt64(ObjCRuntime.Dlfcn.dlopen("/System/Library/Frameworks/UIKit.framework/UIKit", 0), "UIAccessibilityTraitToggleButton")
            : null;
#endif

#if ANDROID
    private void OnKeyPress(object? sender, Android.Views.View.KeyEventArgs e)
    {
        var activate = e.KeyCode is Android.Views.Keycode.Enter or Android.Views.Keycode.Space
            or Android.Views.Keycode.DpadCenter or Android.Views.Keycode.NumpadEnter;
        e.Handled = activate;
        if (activate && e.Event?.Action == Android.Views.KeyEventActions.Up)
            Toggle();
    }

    /// <summary>
    /// Reports the row as a checkable Switch and toggles it on TalkBack's or Switch Access's
    /// click; whatever MAUI had set still handles everything else.
    /// </summary>
    private sealed class SwitchDelegate(SwitchRow row, AndroidX.Core.View.AccessibilityDelegateCompat? inner)
        : AndroidX.Core.View.AccessibilityDelegateCompat
    {
        public override void OnInitializeAccessibilityNodeInfo(Android.Views.View? host,
            AndroidX.Core.View.Accessibility.AccessibilityNodeInfoCompat? info)
        {
            if (host is null || info is null)
                return;
            if (inner is not null)
                inner.OnInitializeAccessibilityNodeInfo(host, info);
            else
                base.OnInitializeAccessibilityNodeInfo(host, info);
            info.ClassName = "android.widget.Switch";
            info.Checkable = true;
            info.Checked = row.IsToggled;
            info.Enabled = row.IsEnabled;
            info.Clickable = row.IsEnabled;
        }

        public override bool PerformAccessibilityAction(Android.Views.View? host, int action, Android.OS.Bundle? args)
        {
            if (action == AndroidX.Core.View.Accessibility.AccessibilityNodeInfoCompat.ActionClick)
            {
                row.Toggle();
                return true;
            }
            return inner?.PerformAccessibilityAction(host, action, args) ?? base.PerformAccessibilityAction(host, action, args);
        }
    }
#endif
}
