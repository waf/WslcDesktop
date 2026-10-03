using Aprillz.MewUI;
using Aprillz.MewUI.Controls;

using WslcGui.App.Icons;
using WslcGui.App.Pages;
using WslcGui.Core;

namespace WslcGui.App.Shell;

internal sealed class MainWindow : Window
{
    private static readonly TimeSpan s_pollInterval = TimeSpan.FromSeconds(4);

    private readonly DispatcherTimer _pollTimer = new(s_pollInterval);
    private readonly NavigationView _navigation;
    private IRefreshablePage? _currentPage;

    public MainWindow(EngineStatusViewModel engineStatus, ContainersViewModel containers, ImagesViewModel images)
    {
        this.Title("WSLC Desktop").Resizable(1200, 760);

        _navigation = BuildNavigation(new ContainersPage(containers), new ImagesPage(images));
        var navigation = _navigation;
        Content = new DockPanel().Children(
            BuildStatusBar(engineStatus).DockBottom(),
            navigation);

        // Refresh the page being shown: once when it's opened, then periodically in the background.
        // Background refreshes are skipped while the engine VM is idle so the app never keeps it awake.
        navigation.SelectionChanged += item => ShowPage((item as Page)?.Content as IRefreshablePage);
        _pollTimer.Tick += () =>
        {
            if (WindowState != WindowState.Minimized)
            {
                _ = _currentPage?.RefreshAsync(RefreshReason.Background);
            }
        };
    }

    /// <summary>Starts loading and polling. Call once the dispatcher is running (Application.Run's startup callback).</summary>
    public void Start()
    {
        _pollTimer.Start();
        ShowPage((_navigation.SelectedItem as Page)?.Content as IRefreshablePage);
    }

    private void ShowPage(IRefreshablePage? page)
    {
        _currentPage = page;
        _ = page?.RefreshAsync(RefreshReason.User);
    }

    private static NavigationView BuildNavigation(ContainersPage containers, ImagesPage images)
    {
        var navigation = new NavigationView
        {
            PaneWidth = 220,
            PaneDisplayMode = PaneDisplayMode.Auto,
        };

        Page[] pages =
        [
            new("Containers", IconData.Cube, containers),
            new("Images", IconData.Layer, images),
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

    // TODO(M6): replace with the real pages.
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
