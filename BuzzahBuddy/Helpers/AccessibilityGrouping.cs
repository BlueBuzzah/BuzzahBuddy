namespace BuzzahBuddy.Helpers;

public static class AccessibilityGrouping
{
    /// <summary>
    /// Makes VoiceOver read this view's children as one group, so side-by-side panes are read
    /// pane by pane, not interleaved row by row; VoiceOver follows tree order within a group. iOS only:
    /// TalkBack orders sibling views by on-screen position (top, then left), so Android reads
    /// side-by-side panes left to right and needs nothing.
    /// </summary>
    public static void GroupForScreenReader(this View view)
    {
#if IOS
        void Apply()
        {
            if (view.Handler?.PlatformView is UIKit.UIView native)
                native.ShouldGroupAccessibilityChildren = true;
        }
        view.HandlerChanged += (_, _) => Apply();
        Apply();
#endif
    }
}
