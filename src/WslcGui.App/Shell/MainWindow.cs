using Aprillz.MewUI;
using Aprillz.MewUI.Controls;

using WslcGui.App.Icons;
using WslcGui.App.Pages;
using WslcGui.Core;

namespace WslcGui.App.Shell;

/// <summary>The pages the main window hosts.</summary>
internal sealed record MainWindowPages(
    ContainersPage Containers,
    ImagesPage Images,
    VolumesPage Volumes,
    NetworksPage Networks,
    TroubleshootPage Troubleshoot,
    SettingsPage Settings);

internal sealed class MainWindow : Window
{
    private static readonly TimeSpan s_pollInterval = TimeSpan.FromSeconds(4);

    private readonly DispatcherTimer _pollTimer = new(s_pollInterval);
    private readonly NavigationView _navigation;
    private readonly ContainersViewModel _containers;
    private readonly Func<AppSettings> _settings;
    private IRefreshablePage? _currentPage;
    private TrayIcon? _tray;
    private bool _quitting;

    public MainWindow(EngineStatusViewModel engineStatus, ContainersViewModel containers, MainWindowPages pages, Func<AppSettings> settings)
    {
        _containers = containers;
        _settings = settings;
        this.Title("WSLC Desktop").Resizable(1200, 760);
        Icon = IconSource.FromResource<MainWindow>("WslcGui.app.ico");

        _navigation = BuildNavigation(pages);
        Content = new DockPanel().Children(
            BuildStatusBar(engineStatus).DockBottom(),
            _navigation);

        // Refresh the page being shown: once when it's opened, then periodically in the background.
        // Background refreshes are skipped while the engine VM is idle so the app never keeps it awake.
        _navigation.SelectionChanged += item => ShowPage((item as Page)?.Content as IRefreshablePage);
        _pollTimer.Tick += () =>
        {
            if (IsVisible && WindowState != WindowState.Minimized)
            {
                _ = engineStatus.RefreshRuntimeStateAsync();
                _ = _currentPage?.RefreshAsync(RefreshReason.Background);
            }
        };

        Closing += e =>
        {
            if (!_quitting && _settings().CloseToTray && _tray is not null)
            {
                e.Cancel = true;
                _currentPage?.SetActive(false);
                Hide();
            }
        };
        Closed += () => _tray?.Dispose();
        _containers.RowsChanged += UpdateTrayTooltip;
    }

    /// <summary>Starts loading, polling and the tray icon. Call once the dispatcher is running (Application.Run's startup callback).</summary>
    public void Start()
    {
        _pollTimer.Start();
        ShowPage((_navigation.SelectedItem as Page)?.Content as IRefreshablePage);
        Loaded += () =>
        {
            _tray = new TrayIcon(this, "WSLC Desktop", BuildTrayMenu);
            _tray.Show();
            UpdateTrayTooltip();
        };
    }

    /// <summary>Exits even when closing to the tray is on.</summary>
    public void Quit()
    {
        _quitting = true;
        Close();
    }

    private List<TrayMenuItem> BuildTrayMenu()
    {
        var running = _containers.Items.Where(r => r.IsRunning).Select(r => r.Name).ToList();
        List<TrayMenuItem> items = [new("Open WSLC Desktop", RestoreFromTray), TrayMenuItem.Separator];
        items.Add(new(running.Count switch { 0 => "No containers running", 1 => "1 container running", var n => $"{n} containers running" }));
        items.AddRange(running.Take(10).Select(name => new TrayMenuItem($"    {name}")));
        items.AddRange([TrayMenuItem.Separator, new("Quit", Quit)]);
        return items;
    }

    private void RestoreFromTray()
    {
        _tray?.ShowWindow();
        _currentPage?.SetActive(true);
        _ = _currentPage?.RefreshAsync(RefreshReason.User);
    }

    private void UpdateTrayTooltip()
    {
        var running = _containers.Items.Count(r => r.IsRunning);
        _tray?.UpdateTooltip(running == 0 ? "WSLC Desktop" : $"WSLC Desktop — {running} running");
    }

    private void ShowPage(IRefreshablePage? page)
    {
        _currentPage?.SetActive(false);
        _currentPage = page;
        page?.SetActive(true);
        _ = page?.RefreshAsync(RefreshReason.User);
    }

    private static NavigationView BuildNavigation(MainWindowPages pages)
    {
        var navigation = new NavigationView
        {
            PaneWidth = 220,
            PaneDisplayMode = PaneDisplayMode.Auto,
        };

        Page[] main =
        [
            new("Containers", IconData.Cube, pages.Containers),
            new("Images", IconData.Layer, pages.Images),
            new("Volumes", IconData.Storage, pages.Volumes),
            new("Networks", IconData.Globe, pages.Networks),
        ];
        navigation.Items(main, p => p.Title, icon: p => IconFactory.Geometry(p.Icon), content: p => p.Content);

        Page[] footer =
        [
            new("Troubleshoot", IconData.Wrench, pages.Troubleshoot),
            new("Settings", IconData.Settings, pages.Settings),
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

    private sealed record Page(string Title, string Icon, FrameworkElement Content);
}
