namespace BuzzahBuddy.Helpers;

/// <summary>
/// Font plumbing for the design system's type styles: which font file each style's
/// family key points at (heavier under Bold Text), and tabular numerals for numbers
/// that change (timers, battery levels).
/// </summary>
public static class Typography
{
    // Resource key → (normal alias, Bold Text alias). Type.g.xaml styles read these keys.
    private static readonly (string Key, string Normal, string Bold)[] Families =
    {
        ("FontDisplay500", "LexendMedium", "LexendBold"),
        ("FontDisplay600", "LexendSemiBold", "LexendBold"),
        ("FontSans400", "AtkinsonRegular", "AtkinsonBold"),
        ("FontSans700", "AtkinsonBold", "AtkinsonExtraBold"),
        ("FontMono400", "AtkinsonMonoRegular", "AtkinsonMonoBold"),
    };

    /// <summary>Points every family key at its normal or Bold Text font.</summary>
    public static void ApplyFontWeights(bool boldText)
    {
        if (Application.Current is not { } app)
            return;
        foreach (var (key, normal, bold) in Families)
            app.Resources[key] = boldText ? bold : normal;
    }

    public static readonly BindableProperty TabularNumbersProperty = BindableProperty.CreateAttached(
        "TabularNumbers", typeof(bool), typeof(Typography), false);

    public static bool GetTabularNumbers(BindableObject view) => (bool)view.GetValue(TabularNumbersProperty);

    public static void SetTabularNumbers(BindableObject view, bool value) => view.SetValue(TabularNumbersProperty, value);

    /// <summary>Applies tabular numerals after MAUI sets a label's font.</summary>
    public static void RegisterHandlers()
    {
        Microsoft.Maui.Handlers.LabelHandler.Mapper.AppendToMapping(nameof(ILabel.Font), (handler, label) =>
        {
            if (label is not BindableObject view || !GetTabularNumbers(view))
                return;
#if ANDROID
            handler.PlatformView.FontFeatureSettings = "tnum";
#elif IOS
            var font = handler.PlatformView.Font;
            var descriptor = font.FontDescriptor.CreateWithAttributes(new UIKit.UIFontAttributes(
                new UIKit.UIFontFeature(CoreText.CTFontFeatureNumberSpacing.Selector.MonospacedNumbers)));
            if (UIKit.UIFont.FromDescriptor(descriptor, 0) is { } tabular)
                handler.PlatformView.Font = tabular;
#endif
        });
    }
}
