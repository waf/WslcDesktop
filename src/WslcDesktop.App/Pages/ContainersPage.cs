using Aprillz.MewUI;
using Aprillz.MewUI.Controls;
using Aprillz.MewUI.Rendering;

using WslcDesktop.App.Dialogs;
using WslcDesktop.App.Icons;
using WslcDesktop.Core;
using WslcDesktop.Engine;

namespace WslcDesktop.App.Pages;

internal sealed class ContainersPage : UserControl, IRefreshablePage
{
    private static readonly Color s_running = Color.FromRgb(46, 160, 67);
    private static readonly Color s_transitioning = Color.FromRgb(210, 153, 34);
    private static readonly Color s_stopped = Color.FromRgb(140, 140, 140);

    private readonly ContainersViewModel _vm;
    private readonly DialogService _dialogs;
    private readonly Func<ContainerRow, ContainerDetailsViewModel> _createDetails;
    private readonly GridView _grid;
    private readonly Border _host = new();
    private FrameworkElement? _listView;
    private ContainerDetailsViewModel? _details;
    private readonly StatsMonitor _stats;
    private IDisposable? _statsWatch;

    public ContainersPage(ContainersViewModel vm, DialogService dialogs, Func<ContainerRow, ContainerDetailsViewModel> createDetails, StatsMonitor stats)
    {
        _vm = vm;
        _stats = stats;
        _dialogs = dialogs;
        _createDetails = createDetails;
        _grid = BuildGrid();
        _grid.ItemDoubleClicked += item =>
        {
            if (item is ContainerRow row)
            {
                ShowDetails(row);
            }
        };
        Build();
    }

    /// <summary>Shows one container's details in place of the list.</summary>
    public void ShowDetails(ContainerRow row)
    {
        CloseDetails();
        _details = _createDetails(row);
        _host.Child = new ContainerDetailsView(_details, ShowList);
    }

    private void ShowList()
    {
        CloseDetails();
        _host.Child = _listView;
    }

    private void CloseDetails()
    {
        _details?.Dispose();
        _details = null;
    }

    public Task RefreshAsync(RefreshReason reason) => _vm.RefreshAsync(reason);

    /// <summary>Resource usage (list columns and the details Stats tab) is sampled only while this page is shown.</summary>
    public void SetActive(bool active)
    {
        if (active)
        {
            _statsWatch ??= _stats.Watch();
        }
        else
        {
            _statsWatch?.Dispose();
            _statsWatch = null;
        }
    }

    protected override Element OnBuild()
    {
        // Row context menu: acts on the selection if the clicked row is part of it, else on the clicked row.
        var start = new Command("containers.start", "Start");
        var stop = new Command("containers.stop", "Stop");
        var restart = new Command("containers.restart", "Restart");
        var kill = new Command("containers.kill", "Kill");
        var remove = new Command("containers.remove", "Remove");
        var details = new Command("containers.details", "Details");
        var copyId = new Command("containers.copyId", "Copy ID");
        var copyName = new Command("containers.copyName", "Copy name");
        Commands.Register(start, (ContainerRow row) => _ = _vm.StartAsync(PageParts.Targets(_grid, row)), (ContainerRow row) => !row.IsRunning);
        Commands.Register(stop, (ContainerRow row) => _ = _vm.StopAsync(PageParts.Targets(_grid, row)), (ContainerRow row) => row.IsRunning);
        Commands.Register(restart, (ContainerRow row) => _ = _vm.RestartAsync(PageParts.Targets(_grid, row)));
        Commands.Register(kill, (ContainerRow row) => _ = _vm.KillAsync(PageParts.Targets(_grid, row)), (ContainerRow row) => row.IsRunning);
        Commands.Register(remove, (ContainerRow row) => _ = _vm.RemoveAsync(PageParts.Targets(_grid, row)));
        Commands.Register(details, (ContainerRow row) => ShowDetails(row));
        Commands.Register(copyId, (ContainerRow row) => PageParts.CopyToClipboard(row.Id));
        Commands.Register(copyName, (ContainerRow row) => PageParts.CopyToClipboard(row.Name));

        var rowMenu = new ContextMenu()
            .Item(details)
            .Separator()
            .Item(start)
            .Item(stop)
            .Item(restart)
            .Item(kill)
            .Separator()
            .Item(remove)
            .Separator()
            .Item(copyId)
            .Item(copyName);
        _grid.PrepareContainer<ContainerRow>((row, _, _, _) => row.ContextMenu = rowMenu);

        // Toolbar: acts on the selection.
        var startButton = PageParts.ToolButton(IconData.Play, "Start", () => _ = _vm.StartAsync(Selected()));
        var stopButton = PageParts.ToolButton(IconData.Stop, "Stop", () => _ = _vm.StopAsync(Selected()));
        var restartButton = PageParts.ToolButton(IconData.ArrowClockwise, "Restart", () => _ = _vm.RestartAsync(Selected()));
        var removeButton = PageParts.ToolButton(IconData.Delete, "Remove", () => _ = _vm.RemoveAsync(Selected()));
        void UpdateButtons()
        {
            var selected = Selected();
            startButton.IsEnabled = selected.Any(r => !r.IsRunning);
            stopButton.IsEnabled = selected.Any(r => r.IsRunning);
            restartButton.IsEnabled = selected.Count > 0;
            removeButton.IsEnabled = selected.Count > 0;
        }

        _grid.SelectedIndicesChanged += UpdateButtons;
        _vm.Items.CollectionChanged += (_, _) => UpdateButtons();
        UpdateButtons();

        var runButton = PageParts.ToolButton(IconData.Add, "Run…", () => _ = _dialogs.ShowRunAsync());
        var pruneButton = PageParts.ToolButton(IconData.Broom, "Remove stopped", () => _ = _vm.PruneAsync());
        var refreshButton = PageParts.ToolButton(IconData.ArrowClockwise, "Refresh", () => _ = _vm.RefreshAsync());
        var showAll = new CheckBox().Content("Show stopped").IsChecked(_vm.ShowAll).CenterVertical().Margin(8, 0)
            .OnCheckedChanged(isChecked => _vm.ShowAll = isChecked);

        _listView = new Grid()
            .Rows("Auto, Auto, *")
            .Children(
                PageParts.Header("Containers", runButton, startButton, stopButton, restartButton, removeButton),
                new DockPanel().Row(1).Padding(PageParts.Inset, 0, PageParts.Inset, 8).Spacing(8).Children(
                    new StackPanel().DockRight().Horizontal().Spacing(4).Children(pruneButton, refreshButton),
                    new StackPanel().DockLeft().Horizontal().Spacing(8).Children(
                        PageParts.SearchBox("Search name, image or ID", text => _vm.SearchText = text),
                        showAll),
                    PageParts.StatusLine(_vm)),
                _grid.Row(2).Margin(PageParts.Inset, 0, PageParts.Inset, 16),
                PageParts.EmptyHint(_vm, "No containers yet. Use Run… to start one.").Row(2));

        _host.Child = _listView;
        return _host;
    }

    private List<ContainerRow> Selected() => PageParts.Targets<ContainerRow>(_grid).ToList();

    private GridView BuildGrid()
    {
        var grid = new GridView
        {
            ItemsSource = ItemsView.Create(_vm.Items, row => row.Name, row => row.Id),
            SelectionMode = ItemsSelectionMode.Extended,
            ZebraStriping = false,
            ShowGridLines = false,
        };

        grid.Columns(
            new GridViewColumn<ContainerRow>()
                .Header("Name")
                .StarWidth(2, minWidth: 150)
                .SortBy(row => row.Name, StringComparer.OrdinalIgnoreCase)
                .Bind(
                    _ => new StackPanel().Horizontal().Spacing(8).Margin(8, 0).CenterVertical().Children(
                        new Ellipse().Size(8, 8).CenterVertical(),
                        new TextBlock().CenterVertical()),
                    (StackPanel cell, ContainerRow row) =>
                    {
                        ((Ellipse)cell.Children[0]).Fill = new SolidColorBrush(StateColor(row));
                        ((TextBlock)cell.Children[1]).Text = row.Name;
                    }),
            PageParts.StarTextColumn<ContainerRow, string>("Image", 2, 120, row => row.Image, row => row.Image),
            PageParts.TextColumn<ContainerRow, string>("Status", 170, row => row.StatusText, row => row.StatusText),
            PageParts.TextColumn<ContainerRow, double>("CPU", 70, row => row.CpuText, row => row.CpuPercent ?? -1, alignRight: true),
            PageParts.TextColumn<ContainerRow, long>("Memory", 90, row => row.MemoryText, row => row.MemoryBytes ?? -1, alignRight: true),
            PageParts.TextColumn<ContainerRow, string>("Ports", 110, row => row.PortsText, row => row.PortsText),
            PageParts.TextColumn<ContainerRow, DateTimeOffset>("Created", 110, row => row.CreatedText, row => row.CreatedAt ?? DateTimeOffset.MinValue),
            PageParts.TextColumn<ContainerRow, string>("ID", 110, row => row.ShortId, row => row.Id));

        return grid;
    }

    private static Color StateColor(ContainerRow row) => row switch
    {
        { Pending: not null } => s_transitioning,
        { State: ContainerState.Running } => s_running,
        { State: ContainerState.Restarting or ContainerState.Paused or ContainerState.Created } => s_transitioning,
        _ => s_stopped,
    };
}
