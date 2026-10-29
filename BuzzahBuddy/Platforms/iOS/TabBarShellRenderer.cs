using System.Runtime.CompilerServices;
using Microsoft.Maui.Controls.Handlers.Compatibility;
using Microsoft.Maui.Controls.Platform;
using Microsoft.Maui.Controls.Platform.Compatibility;
using Microsoft.Maui.Platform;
using UIKit;

namespace BuzzahBuddy;

/// <summary>
/// Colors unselected tab icons like their titles. The iOS 26 tab bar ignores MAUI's unselected
/// color (and UnselectedItemTintColor) for icons and draws them in the system label color, so
/// each tab gets a pre-colored unselected image and a template selected image the bar tints.
/// </summary>
public class TabBarShellRenderer : ShellRenderer
{
    private sealed record Colored(UIImage Template, UIImage Image, UIColor Color);

    private static readonly ConditionalWeakTable<UITabBarItem, Colored> Icons = new();
    private static UIColor? _unselected;

    protected override IShellTabBarAppearanceTracker CreateTabBarAppearanceTracker() => new TabBarTracker();

    protected override IShellItemRenderer CreateShellItemRenderer(ShellItem item) => new TabBarItemRenderer(this) { ShellItem = item };

    /// <summary>
    /// Colors icons that are new from MAUI, or all of them after a color change. Unchanged
    /// icons are left alone: assigning one re-lays out the bar, which would call back here.
    /// </summary>
    private static void ColorIcons(UITabBar tabBar)
    {
        if (_unselected is not { } color)
            return;
        foreach (var item in tabBar.Items ?? [])
        {
            Icons.TryGetValue(item, out var done);
            var fresh = item.Image is { } image && image.Handle != done?.Image.Handle;
            if (!fresh && (done is null || done.Color.Equals(color)))
                continue;
            var template = fresh ? item.Image!.ImageWithRenderingMode(UIImageRenderingMode.AlwaysTemplate) : done!.Template;
            var colored = template.ApplyTintColor(color).ImageWithRenderingMode(UIImageRenderingMode.AlwaysOriginal);
            item.SelectedImage = template;
            item.Image = colored;
            Icons.AddOrUpdate(item, new Colored(template, colored, color));
        }
    }

    private sealed class TabBarTracker : ShellTabBarAppearanceTracker
    {
        // Runs again whenever the Shell's colors change, so a theme switch recolors too.
        public override void SetAppearance(UITabBarController controller, ShellAppearance appearance)
        {
            base.SetAppearance(controller, appearance);
            if (((IShellAppearanceElement)appearance).EffectiveTabBarUnselectedColor is not { } unselected)
                return;
            _unselected = unselected.ToPlatform();
            ColorIcons(controller.TabBar);
        }
    }

    // Tab icons load asynchronously; layout runs after they land.
    private sealed class TabBarItemRenderer(IShellContext context) : ShellItemRenderer(context)
    {
        public override void ViewDidLayoutSubviews()
        {
            base.ViewDidLayoutSubviews();
            ColorIcons(TabBar);
        }
    }
}
