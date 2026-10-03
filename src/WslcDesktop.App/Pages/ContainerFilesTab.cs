using Aprillz.MewUI;
using Aprillz.MewUI.Controls;
using Aprillz.MewUI.Input;
using Aprillz.MewUI.Platform;

using WslcDesktop.App.Dialogs;
using WslcDesktop.App.Icons;
using WslcDesktop.Core;

namespace WslcDesktop.App.Pages;

/// <summary>Browse a container's filesystem: folder list on the left, file preview on the right.</summary>
internal sealed class ContainerFilesTab : UserControl
{
    private readonly FilesViewModel _vm;
    private readonly GridView _grid;
    private bool _loaded;

    public ContainerFilesTab(FilesViewModel vm)
    {
        _vm = vm;
        _grid = BuildGrid();
        Build();
    }

    /// <summary>Loads the root folder the first time the tab is shown.</summary>
    public void Activate()
    {
        if (!_loaded)
        {
            _loaded = true;
            _ = _vm.NavigateAsync("/");
        }
    }

    protected override Element OnBuild()
    {
        var path = new TextBox().Text(_vm.CurrentPath).FontFamily("Cascadia Mono");
        path.KeyDown += e =>
        {
            if (e.Key == Key.Enter)
            {
                e.Handled = true;
                _ = _vm.NavigateAsync(path.Text);
            }
        };

        var up = new Button().StyleName("flat-button").Padding(10, 6).ToolTip("Parent folder").Content("↑ Up").OnClick(() => _ = _vm.UpAsync());
        var refresh = PageParts.ToolButton(IconData.ArrowClockwise, "Refresh", () => _ = _vm.RefreshAsync());
        var download = PageParts.ToolButton(IconData.ArrowDownload, "Download…", () => _ = DownloadAsync());
        var upload = PageParts.ToolButton(IconData.Add, "Upload…", () => _ = UploadAsync());

        var preview = new MultiLineTextBox().IsReadOnly().Wrap(false).FontFamily("Cascadia Mono");
        preview.Document.UndoSizeLimit = 0;
        var previewTitle = new TextBlock().SemiBold().Margin(0, 0, 0, 6);

        void UpdateButtons()
        {
            download.IsEnabled = _grid.SelectedItems.Count > 0;
            upload.IsEnabled = _vm.CanUpload;
            upload.ToolTip(_vm.CanUpload ? "Copy files into this folder (or drop them on the list)" : "Start the container to upload files");
        }

        _vm.PropertyChanged += (_, e) =>
        {
            switch (e.PropertyName)
            {
                case nameof(_vm.CurrentPath):
                    path.Text(_vm.CurrentPath);
                    break;
                case nameof(_vm.CanUpload):
                    UpdateButtons();
                    break;
                case nameof(_vm.PreviewText):
                    preview.Text(_vm.PreviewText ?? string.Empty);
                    break;
                case nameof(_vm.PreviewTitle):
                    previewTitle.Text = _vm.PreviewTitle ?? "Preview";
                    break;
            }
        };
        _grid.SelectedIndicesChanged += UpdateButtons;
        UpdateButtons();
        previewTitle.Text = "Preview";
        preview.Text("Double-click a file to preview it.");

        // Files dropped from Explorer are uploaded into the current folder.
        _grid.AllowDrop = true;
        _grid.Drop += e =>
        {
            if (_vm.CanUpload && e.Data.TryGetData(DataFormats.StorageItems, out var paths) && paths.Count > 0)
            {
                _ = _vm.UploadAsync(paths.ToList());
            }
        };

        var status = new StackPanel().Vertical().Spacing(4).Margin(0, 0, 0, 6).Children(
            FormParts.BoundText(_vm, nameof(_vm.ModeText), x => x.ModeText),
            FormParts.BoundText(_vm, nameof(_vm.LoadingText), x => x.LoadingText),
            FormParts.BoundText(_vm, nameof(_vm.Error), x => x.Error, FormParts.ErrorColor));

        var split = new SplitPanel
        {
            FirstLength = new GridLength(3, GridUnitType.Star),
            MinFirst = 300,
            MinSecond = 200,
            First = _grid,
            Second = new DockPanel().Margin(8, 0, 0, 0).Children(previewTitle.DockTop(), preview),
        }.Horizontal();

        return new DockPanel().Padding(0, 8, 0, 0).Children(
            new DockPanel().DockTop().Spacing(6).Margin(0, 0, 0, 8).Children(
                new StackPanel().DockLeft().Horizontal().Spacing(2).Children(up),
                new StackPanel().DockRight().Horizontal().Spacing(2).Children(refresh, download, upload),
                path.CenterVertical()),
            status.DockTop(),
            split);
    }

    private GridView BuildGrid()
    {
        var grid = new GridView
        {
            ItemsSource = ItemsView.Create(_vm.Entries, row => row.Name, row => row.Path),
            SelectionMode = ItemsSelectionMode.Extended,
            ZebraStriping = false,
            ShowGridLines = false,
        };

        grid.Columns(
            new GridViewColumn<FileRow>()
                .Header("Name")
                .StarWidth(3, minWidth: 180)
                .SortBy(row => row.SortKey, StringComparer.Ordinal)
                .Bind(
                    _ => new StackPanel().Horizontal().Spacing(8).Margin(8, 0).CenterVertical().Children(
                        new Border().Width(16).Height(16).CenterVertical(),
                        new TextBlock().CenterVertical()),
                    (StackPanel cell, FileRow row) =>
                    {
                        ((Border)cell.Children[0]).Child = IconFactory.Create(row.IsDirectory ? IconData.Folder : row.CanOpen ? IconData.Link : IconData.TextDescription, 16);
                        ((TextBlock)cell.Children[1]).Text = row.DisplayName;
                    }),
            PageParts.TextColumn<FileRow, long>("Size", 90, row => row.SizeText, row => row.Entry.SizeBytes, alignRight: true),
            PageParts.TextColumn<FileRow, DateTimeOffset>("Modified", 140, row => row.ModifiedText, row => row.Entry.Modified ?? DateTimeOffset.MinValue),
            PageParts.TextColumn<FileRow, string>("Permissions", 110, row => row.Entry.Permissions, row => row.Entry.Permissions));

        grid.ItemDoubleClicked += item =>
        {
            if (item is FileRow row)
            {
                _ = _vm.OpenAsync(row);
            }
        };
        return grid;
    }

    private async Task DownloadAsync()
    {
        var rows = PageParts.Targets<FileRow>(_grid);
        if (rows.Count == 0)
        {
            return;
        }

        var folder = await FileDialog.SelectFolderAsync(new FolderDialogOptions { Owner = FindVisualRoot() as Window, Title = "Download to folder" });
        if (folder is not null)
        {
            await _vm.DownloadAsync(rows, folder);
        }
    }

    private async Task UploadAsync()
    {
        var paths = await FileDialog.OpenFilesAsync(new OpenFileDialogOptions { Owner = FindVisualRoot() as Window, Title = $"Upload to {_vm.CurrentPath}" });
        if (paths is { Length: > 0 })
        {
            await _vm.UploadAsync(paths);
        }
    }
}
