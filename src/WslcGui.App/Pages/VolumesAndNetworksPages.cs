using Aprillz.MewUI;
using Aprillz.MewUI.Controls;

using WslcGui.App.Dialogs;
using WslcGui.App.Icons;
using WslcGui.Core;
using WslcGui.Engine;

namespace WslcGui.App.Pages;

internal sealed class VolumesPage : UserControl, IRefreshablePage
{
    private readonly VolumesViewModel _vm;
    private readonly DialogService _dialogs;
    private readonly GridView _grid;

    public VolumesPage(VolumesViewModel vm, DialogService dialogs)
    {
        _vm = vm;
        _dialogs = dialogs;
        _grid = new GridView
        {
            ItemsSource = ItemsView.Create(_vm.Items, row => row.Name, row => row.Name),
            SelectionMode = ItemsSelectionMode.Extended,
            ZebraStriping = false,
            ShowGridLines = false,
        };
        _grid.Columns(
            PageParts.StarTextColumn<VolumeRow, string>("Name", 2, 200, row => row.DisplayName, row => row.Name),
            PageParts.TextColumn<VolumeRow, string>("Driver", 90, row => row.Driver, row => row.Driver),
            PageParts.StarTextColumn<VolumeRow, string>("Used by", 2, 160, row => row.UsageText, row => row.UsageText));
        _grid.ItemDoubleClicked += item =>
        {
            if (item is VolumeRow row)
            {
                _ = InspectAsync(row);
            }
        };
        Build();
    }

    public Task RefreshAsync(RefreshReason reason) => _vm.RefreshAsync(reason);

    protected override Element OnBuild()
    {
        var inspect = new Command("volumes.inspect", "Inspect");
        var remove = new Command("volumes.remove", "Remove");
        var copyName = new Command("volumes.copyName", "Copy name");
        Commands.Register(inspect, (VolumeRow row) => _ = InspectAsync(row));
        Commands.Register(remove, (VolumeRow row) => _ = _vm.RemoveAsync(PageParts.Targets(_grid, row)));
        Commands.Register(copyName, (VolumeRow row) => PageParts.CopyToClipboard(row.Name));
        var rowMenu = new ContextMenu().Item(inspect).Separator().Item(remove).Separator().Item(copyName);
        _grid.PrepareContainer<VolumeRow>((row, _, _, _) => row.ContextMenu = rowMenu);

        var create = PageParts.ToolButton(IconData.Add, "Create…", () => _ = CreateAsync());
        var removeButton = PageParts.ToolButton(IconData.Delete, "Remove", () => _ = _vm.RemoveAsync(PageParts.Targets<VolumeRow>(_grid)));
        void UpdateButtons() => removeButton.IsEnabled = _grid.SelectedItems.Count > 0;
        _grid.SelectedIndicesChanged += UpdateButtons;
        _vm.Items.CollectionChanged += (_, _) => UpdateButtons();
        UpdateButtons();

        var pruneMenu = new ContextMenu { Placement = MenuPlacement.Below };
        var pruneAnonymous = new Command("volumes.pruneAnonymous", "Remove unused anonymous volumes");
        var pruneAll = new Command("volumes.pruneAll", "Remove all unused volumes");
        Commands.Register(pruneAnonymous, () => _ = _vm.PruneAsync(all: false));
        Commands.Register(pruneAll, () => _ = _vm.PruneAsync(all: true));
        pruneMenu.Item(pruneAnonymous).Item(pruneAll);
        var prune = PageParts.ToolButton(IconData.Broom, "Clean up", () => { });
        prune.OnClick(() => pruneMenu.Show(prune));

        return new Grid()
            .Rows("Auto, Auto, *")
            .Children(
                PageParts.Header("Volumes", create, removeButton),
                new DockPanel().Row(1).Padding(24, 0, 24, 8).Spacing(8).Children(
                    new StackPanel().DockRight().Horizontal().Spacing(4).Children(
                        prune,
                        PageParts.ToolButton(IconData.ArrowClockwise, "Refresh", () => _ = _vm.RefreshAsync())),
                    PageParts.SearchBox("Search name or container", text => _vm.SearchText = text).DockLeft(),
                    PageParts.StatusLine(_vm)),
                _grid.Row(2).Margin(16, 0, 16, 16),
                PageParts.EmptyHint(_vm, "No volumes. Volumes keep container data across container removal.").Row(2));
    }

    private async Task CreateAsync()
    {
        if (await _dialogs.PromptAsync("Create a volume", "Volume name", "e.g. pgdata", "Create") is { } result)
        {
            await _vm.CreateAsync(result.Text);
        }
    }

    private async Task InspectAsync(VolumeRow row)
    {
        try
        {
            await _dialogs.ShowTextAsync($"Volume {row.DisplayName}", await _vm.InspectAsync(row));
        }
        catch (EngineException ex)
        {
            _vm.ReportError($"Couldn't inspect {row.DisplayName}", ex);
        }
    }
}

internal sealed class NetworksPage : UserControl, IRefreshablePage
{
    private readonly NetworksViewModel _vm;
    private readonly DialogService _dialogs;
    private readonly GridView _grid;

    public NetworksPage(NetworksViewModel vm, DialogService dialogs)
    {
        _vm = vm;
        _dialogs = dialogs;
        _grid = new GridView
        {
            ItemsSource = ItemsView.Create(_vm.Items, row => row.Name, row => row.Name),
            SelectionMode = ItemsSelectionMode.Extended,
            ZebraStriping = false,
            ShowGridLines = false,
        };
        _grid.Columns(
            PageParts.StarTextColumn<NetworkRow, string>("Name", 2, 200, row => row.Name, row => row.Name),
            PageParts.TextColumn<NetworkRow, string>("Driver", 100, row => row.Driver, row => row.Driver),
            PageParts.TextColumn<NetworkRow, string>("Type", 130, row => row.KindText, row => row.KindText),
            PageParts.TextColumn<NetworkRow, string>("Network ID", 130, row => row.ShortId, row => row.Id),
            PageParts.TextColumn<NetworkRow, DateTimeOffset>("Created", 130, row => row.CreatedText, row => row.CreatedAt ?? DateTimeOffset.MinValue));
        _grid.ItemDoubleClicked += item =>
        {
            if (item is NetworkRow row)
            {
                _ = InspectAsync(row);
            }
        };
        Build();
    }

    public Task RefreshAsync(RefreshReason reason) => _vm.RefreshAsync(reason);

    protected override Element OnBuild()
    {
        var inspect = new Command("networks.inspect", "Inspect");
        var remove = new Command("networks.remove", "Remove");
        Commands.Register(inspect, (NetworkRow row) => _ = InspectAsync(row));
        Commands.Register(remove, (NetworkRow row) => _ = _vm.RemoveAsync(PageParts.Targets(_grid, row)), (NetworkRow row) => !row.IsBuiltIn);
        var rowMenu = new ContextMenu().Item(inspect).Separator().Item(remove);
        _grid.PrepareContainer<NetworkRow>((row, _, _, _) => row.ContextMenu = rowMenu);

        var create = PageParts.ToolButton(IconData.Add, "Create…", () => _ = CreateAsync());
        var removeButton = PageParts.ToolButton(IconData.Delete, "Remove", () => _ = _vm.RemoveAsync(PageParts.Targets<NetworkRow>(_grid)));
        void UpdateButtons() => removeButton.IsEnabled = PageParts.Targets<NetworkRow>(_grid).Any(r => !r.IsBuiltIn);
        _grid.SelectedIndicesChanged += UpdateButtons;
        _vm.Items.CollectionChanged += (_, _) => UpdateButtons();
        UpdateButtons();

        return new Grid()
            .Rows("Auto, Auto, *")
            .Children(
                PageParts.Header("Networks", create, removeButton),
                new DockPanel().Row(1).Padding(24, 0, 24, 8).Spacing(8).Children(
                    new StackPanel().DockRight().Horizontal().Spacing(4).Children(
                        PageParts.ToolButton(IconData.Broom, "Remove unused", () => _ = _vm.PruneAsync()),
                        PageParts.ToolButton(IconData.ArrowClockwise, "Refresh", () => _ = _vm.RefreshAsync())),
                    PageParts.SearchBox("Search name or ID", text => _vm.SearchText = text).DockLeft(),
                    PageParts.StatusLine(_vm)),
                _grid.Row(2).Margin(16, 0, 16, 16),
                PageParts.EmptyHint(_vm, "No networks.").Row(2));
    }

    private async Task CreateAsync()
    {
        if (await _dialogs.PromptAsync("Create a network", "Network name", "e.g. backend", "Create", "Internal (no access outside the network)") is { } result)
        {
            await _vm.CreateAsync(result.Text, result.Option);
        }
    }

    private async Task InspectAsync(NetworkRow row)
    {
        try
        {
            await _dialogs.ShowTextAsync($"Network {row.Name}", await _vm.InspectAsync(row));
        }
        catch (EngineException ex)
        {
            _vm.ReportError($"Couldn't inspect {row.Name}", ex);
        }
    }
}
