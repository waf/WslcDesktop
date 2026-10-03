using Aprillz.MewUI;
using Aprillz.MewUI.Controls;

using WslcGui.App.Icons;
using WslcGui.Core;

namespace WslcGui.App.Shell;

internal sealed class MainWindow : Window
{
    public MainWindow(EngineStatusViewModel engineStatus)
    {
        this.Title("WSLC Desktop").Resizable(1200, 760);
        Content = new DockPanel().Children(
            BuildStatusBar(engineStatus).DockBottom(),
            BuildNavigation());
    }

    private static NavigationView BuildNavigation()
    {
        var navigation = new NavigationView
        {
            PaneWidth = 220,
            PaneDisplayMode = PaneDisplayMode.Auto,
        };

        Page[] pages =
        [
            new("Containers", IconData.Cube, PlaceholderPage("Containers")),
            new("Images", IconData.Layer, PlaceholderPage("Images")),
            new("Volumes", IconData.Storage, PlaceholderPage("Volumes")),
            new("Networks", IconData.Globe, PlaceholderPage("Networks")),
        ];
        navigation.Items(pages, p => p.Title, icon: p => IconFactory.Geometry(p.Icon), content: p => p.Content);

        Page[] footer =
        [
            new("Troubleshoot", IconData.Wrench, PlaceholderPage("Troubleshoot")),
            new("Settings", IconData.Settings, PlaceholderPage("Settings")),
        ];
        navigation.FooterItems(footer, p => p.Title, icon: p => IconFactory.Geometry(p.Icon), content: p => p.Content);
        navigation.SelectedIndex = 0;
        return navigation;
    }

    private static Border BuildStatusBar(EngineStatusViewModel engineStatus) =>
        new Border()
            .BorderThickness(new Thickness(0, 1, 0, 0))
            .Padding(12, 4)
            .WithTheme((theme, border) => border.BorderBrush(theme.Palette.ControlBorder))
            .Child(new TextBlock()
                .Bind(TextBlock.TextProperty, engineStatus, x => x.StatusText)
                .CenterVertical());

    // TODO(M1): replace with the real pages.
    private static StackPanel PlaceholderPage(string title) =>
        new StackPanel()
            .Vertical()
            .Padding(28, 22)
            .Spacing(8)
            .Children(
                new TextBlock().Text(title).FontSize(ThemeFontSize.Medium).SemiBold(),
                new TextBlock().Text("Coming soon."));

    private sealed record Page(string Title, string Icon, FrameworkElement Content);
}
