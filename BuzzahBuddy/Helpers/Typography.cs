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

    /// <summary>
    /// Marks a heading style. On iOS, MAUI scales every font on the body text curve, so headings
    /// grew to about 3× at AX5. Headings follow Apple's Large Title curve instead, never smaller
    /// than body text. Android 14+ already scales large text less, so it's left alone there.
    /// </summary>
    public static readonly BindableProperty HeadingProperty = BindableProperty.CreateAttached(
        "Heading", typeof(bool), typeof(Typography), false);

    public static bool GetHeading(BindableObject view) => (bool)view.GetValue(HeadingProperty);

    public static void SetHeading(BindableObject view, bool value) => view.SetValue(HeadingProperty, value);

    // TypeBody's size: the floor a heading never scales below.
    private const double BodySize = 17;

    /// <summary>Applies heading scaling and tabular numerals after MAUI sets a label's font.</summary>
    public static void RegisterHandlers()
    {
        Microsoft.Maui.Handlers.LabelHandler.Mapper.AppendToMapping(nameof(ILabel.Font), (handler, label) =>
        {
            if (label is not BindableObject view)
                return;
#if ANDROID
            if (GetTabularNumbers(view))
                handler.PlatformView.FontFeatureSettings = "tnum";
#elif IOS
            var font = handler.PlatformView.Font;
            if (GetHeading(view) && label.Font.AutoScalingEnabled && label.Font.Size > 0)
            {
                var size = Math.Max(
                    UIKit.UIFontMetrics.GetMetrics(UIKit.UIFontTextStyleExtensions.GetConstant(UIKit.UIFontTextStyle.LargeTitle)!.ToString()).GetScaledValue((nfloat)label.Font.Size),
                    UIKit.UIFontMetrics.DefaultMetrics.GetScaledValue((nfloat)BodySize));
                font = font.WithSize((nfloat)size) ?? font;
            }
            if (GetTabularNumbers(view))
            {
                var descriptor = font.FontDescriptor.CreateWithAttributes(new UIKit.UIFontAttributes(
                    new UIKit.UIFontFeature(CoreText.CTFontFeatureNumberSpacing.Selector.MonospacedNumbers)));
                font = UIKit.UIFont.FromDescriptor(descriptor, 0) ?? font;
            }
            if (font != handler.PlatformView.Font)
                handler.PlatformView.Font = font;
#endif
        });
    }
}
