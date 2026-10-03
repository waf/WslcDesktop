using Aprillz.MewUI;
using Aprillz.MewUI.Controls;

namespace WslcGui.App.Shell;

/// <summary>
/// The dark slate navigation pane. MewUI's NavigationView paints its pane with the theme's container color and has no
/// property for it, so this finds the pane's host border, recolors it, and gives its rows their own hover/selected
/// colors through a scoped StyleSheet (the app-wide selection color stays as it is for grids and lists).
/// </summary>
internal static class SidebarStyle
{
    private static readonly Color Background = Color.FromRgb(30, 41, 59);   // slate-800
    private static readonly Color Foreground = Color.FromRgb(203, 213, 225); // slate-300
    private static readonly Color Hover = Color.FromRgb(255, 255, 255).WithAlpha(20);
    private static readonly Color Selected = Color.FromRgb(37, 99, 235);     // the app icon's blue
    private static readonly Color SelectedText = Color.FromRgb(255, 255, 255);

    public static void Apply(NavigationView navigation)
    {
        if (navigation.Pane.Parent?.Parent is not Border paneHost)
        {
            return;
        }

        // Foreground is inherited by the item text and icons; a binding is the public way to set it on a Border.
        paneHost.Bind(TextElement.ForegroundProperty, new ObservableValue<Color>(Foreground));

        paneHost.StyleSheet = new StyleSheet();
        paneHost.StyleSheet.Define<ItemContainer>(new Style(typeof(ItemContainer))
        {
            Triggers =
            [
                new StateTrigger { Match = VisualStateFlags.Hot, Setters = [Setter.Create(Control.BackgroundProperty, Hover)] },
                new StateTrigger
                {
                    Match = VisualStateFlags.Selected,
                    Setters = [Setter.Create(Control.BackgroundProperty, Selected), Setter.Create(TextElement.ForegroundProperty, SelectedText)],
                },
            ],
        });

        // NavigationView repaints the pane from its own theme callback on every theme change. Callbacks on an element
        // run in registration order and NavigationView registered first, so ours always runs after it and wins.
        paneHost.WithTheme((_, host) => host.Background = Background);

        // The hamburger is a direct child of the NavigationView, not part of the pane.
        ((IVisualTreeHost)navigation).VisitChildren(child =>
        {
            if (child is Button toggle)
            {
                toggle.WithTheme((_, button) => button.Foreground = Foreground);
            }

            return true;
        });
    }
}
