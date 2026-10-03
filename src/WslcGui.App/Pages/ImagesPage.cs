using Aprillz.MewUI;
using Aprillz.MewUI.Controls;

using WslcGui.App.Icons;
using WslcGui.Core;

namespace WslcGui.App.Pages;

internal sealed class ImagesPage : UserControl, IRefreshablePage
{
    private readonly ImagesViewModel _vm;
    private readonly GridView _grid;

    public ImagesPage(ImagesViewModel vm)
    {
        _vm = vm;
        _grid = BuildGrid();
        Build();
    }

    public Task RefreshAsync(RefreshReason reason) => _vm.RefreshAsync(reason);

    protected override Element OnBuild()
    {
        var remove = new Command("images.remove", "Remove");
        var copyReference = new Command("images.copyReference", "Copy name");
        var copyId = new Command("images.copyId", "Copy ID");
        Commands.Register(remove, (ImageRow row) => _ = _vm.RemoveAsync(PageParts.Targets(_grid, row)));
        Commands.Register(copyReference, (ImageRow row) => PageParts.CopyToClipboard(row.Reference));
        Commands.Register(copyId, (ImageRow row) => PageParts.CopyToClipboard(row.Id));

        var rowMenu = new ContextMenu()
            .Item(remove)
            .Separator()
            .Item(copyReference)
            .Item(copyId);
        _grid.PrepareContainer<ImageRow>((row, _, _, _) => row.ContextMenu = rowMenu);

        var removeButton = PageParts.ToolButton(IconData.Delete, "Remove", () => _ = _vm.RemoveAsync(PageParts.Targets<ImageRow>(_grid)));
        void UpdateButtons() => removeButton.IsEnabled = _grid.SelectedItems.Count > 0;
        _grid.SelectedIndicesChanged += UpdateButtons;
        _vm.Items.CollectionChanged += (_, _) => UpdateButtons();
        UpdateButtons();

        var pruneMenu = new ContextMenu { Placement = MenuPlacement.Below };
        var pruneDangling = new Command("images.pruneDangling", "Remove untagged images");
        var pruneAll = new Command("images.pruneAll", "Remove all unused images");
        Commands.Register(pruneDangling, () => _ = _vm.PruneAsync(all: false));
        Commands.Register(pruneAll, () => _ = _vm.PruneAsync(all: true));
        pruneMenu.Item(pruneDangling).Item(pruneAll);
        var pruneButton = PageParts.ToolButton(IconData.Broom, "Clean up", () => { });
        pruneButton.OnClick(() => pruneMenu.Show(pruneButton));

        var refreshButton = PageParts.ToolButton(IconData.ArrowClockwise, "Refresh", () => _ = _vm.RefreshAsync());

        return new Grid()
            .Rows("Auto, Auto, Auto, *")
            .Children(
                PageParts.Header("Images", removeButton),
                new DockPanel().Row(1).Padding(24, 0, 24, 8).Spacing(8).Children(
                    new StackPanel().DockRight().Horizontal().Spacing(4).Children(pruneButton, refreshButton),
                    PageParts.SearchBox("Search name or ID", text => _vm.SearchText = text)),
                PageParts.StatusLine(_vm).Row(2),
                _grid.Row(3).Margin(16, 0, 16, 16),
                PageParts.EmptyHint(_vm, "No images. Pull one with 'wslc pull alpine'.").Row(3));
    }

    private GridView BuildGrid()
    {
        var grid = new GridView
        {
            ItemsSource = ItemsView.Create(_vm.Items, row => row.Reference, row => row.Id + "|" + row.Reference),
            SelectionMode = ItemsSelectionMode.Extended,
            ZebraStriping = false,
            ShowGridLines = false,
        };

        grid.Columns(
            PageParts.StarTextColumn<ImageRow, string>("Repository", 3, 200, row => row.Repository, row => row.Repository),
            PageParts.TextColumn<ImageRow, string>("Tag", 140, row => row.Tag, row => row.Tag),
            PageParts.TextColumn<ImageRow, string>("Image ID", 120, row => row.ShortId, row => row.Id),
            PageParts.TextColumn<ImageRow, string>("Usage", 170, row => row.UsageText, row => row.UsageText),
            PageParts.TextColumn<ImageRow, long>("Size", 100, row => row.SizeText, row => row.SizeBytes, alignRight: true),
            PageParts.TextColumn<ImageRow, DateTimeOffset>("Created", 120, row => row.CreatedText, row => row.CreatedAt ?? DateTimeOffset.MinValue));

        return grid;
    }
}
