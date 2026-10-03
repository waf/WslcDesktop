using System.Collections.ObjectModel;
using System.Globalization;
using System.Text;

using WslcDesktop.Engine;

namespace WslcDesktop.Core;

public sealed record FileRow(ContainerFileEntry Entry)
{
    public string Name => Entry.Name;
    public string Path => Entry.Path;
    public bool IsDirectory => Entry.Kind == ContainerFileKind.Directory;

    /// <summary>Directories and symlinks can be opened (a symlink may point at a directory).</summary>
    public bool CanOpen => Entry.Kind is ContainerFileKind.Directory or ContainerFileKind.Symlink;

    public string KindText => Entry.Kind switch
    {
        ContainerFileKind.Directory => "Folder",
        ContainerFileKind.Symlink => "Link",
        ContainerFileKind.File => "File",
        _ => "Other",
    };

    public string SizeText => Entry.Kind == ContainerFileKind.File ? Formatting.Bytes(Entry.SizeBytes) : string.Empty;

    public string ModifiedText => Entry.Modified?.ToLocalTime().ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture) ?? string.Empty;

    public string DisplayName => Entry.LinkTarget is { } target ? $"{Entry.Name} → {target}" : Entry.Name;

    /// <summary>Folders first, then by name.</summary>
    public string SortKey => (IsDirectory ? "0" : "1") + Entry.Name;
}

/// <summary>
/// Browses a container's filesystem: live listing while it runs, otherwise (or when the image has no find/stat)
/// a read-only snapshot from an export.
/// </summary>
public sealed class FilesViewModel(IContainerFiles files, string containerId, Func<bool> isRunning, IUserInteraction ui) : ObservableObject
{
    /// <summary>Files larger than this aren't previewed.</summary>
    public const long MaxPreviewFileBytes = 2 * 1024 * 1024;

    /// <summary>How much of a file the preview shows.</summary>
    public const int PreviewBytes = 256 * 1024;

    private IContainerFileSnapshot? _snapshot;
    private bool _snapshotForced;
    private string _path = "/";
    private bool _isLoading;
    private string? _error;
    private string? _loadingText;
    private string? _previewTitle;
    private string? _previewText;

    public ObservableCollection<FileRow> Entries { get; } = [];

    public string CurrentPath
    {
        get => _path;
        private set => SetProperty(ref _path, value);
    }

    public bool IsLoading
    {
        get => _isLoading;
        private set => SetProperty(ref _isLoading, value);
    }

    public string? Error
    {
        get => _error;
        private set => SetProperty(ref _error, value);
    }

    /// <summary>Progress note for slow loads (taking a snapshot), or null.</summary>
    public string? LoadingText
    {
        get => _loadingText;
        private set => SetProperty(ref _loadingText, value);
    }

    /// <summary>Showing a snapshot instead of the live filesystem.</summary>
    public bool IsSnapshot => _snapshot is not null;

    /// <summary>Explains snapshot mode, or null when listing live.</summary>
    public string? ModeText => _snapshot is null
        ? null
        : _snapshotForced
            ? "This image has no tools to list files live, so this is a snapshot taken when the tab opened (volumes not included)."
            : "The container is stopped: this is a read-only snapshot of its filesystem (volumes not included).";

    /// <summary>Uploading needs a running container.</summary>
    public bool CanUpload => isRunning();

    public string? PreviewTitle
    {
        get => _previewTitle;
        private set => SetProperty(ref _previewTitle, value);
    }

    public string? PreviewText
    {
        get => _previewText;
        private set => SetProperty(ref _previewText, value);
    }

    public Task NavigateAsync(string path) => LoadAsync(NormalizePath(path));

    public Task UpAsync()
    {
        var path = CurrentPath.TrimEnd('/');
        var slash = path.LastIndexOf('/');
        return LoadAsync(slash <= 0 ? "/" : path[..slash]);
    }

    /// <summary>Re-reads the current directory (and retakes the snapshot in snapshot mode).</summary>
    public Task RefreshAsync()
    {
        _snapshot = null;
        _snapshotForced = false;
        return LoadAsync(CurrentPath);
    }

    /// <summary>Opens a folder, follows a link, or previews a file.</summary>
    public async Task OpenAsync(FileRow row)
    {
        if (row.IsDirectory)
        {
            await LoadAsync(row.Path);
        }
        else if (row.Entry.Kind == ContainerFileKind.Symlink && row.Entry.LinkTarget is { } target)
        {
            var resolved = target.StartsWith('/') ? target : CombinePath(ParentOf(row.Path), target);
            if (!await LoadAsync(NormalizePath(resolved), quietIfNotDirectory: true))
            {
                await PreviewAsync(row);
            }
        }
        else
        {
            await PreviewAsync(row);
        }
    }

    public async Task PreviewAsync(FileRow row)
    {
        PreviewTitle = row.Path;
        if (row.Entry.Kind == ContainerFileKind.File && row.Entry.SizeBytes > MaxPreviewFileBytes)
        {
            PreviewText = $"{Formatting.Bytes(row.Entry.SizeBytes)} is too large to preview. Download it instead.";
            return;
        }

        PreviewText = "Loading…";
        try
        {
            var bytes = await files.ReadFileStartAsync(containerId, row.Path, PreviewBytes);
            PreviewText = DescribeContent(bytes, row.Entry.SizeBytes);
        }
        catch (EngineException ex)
        {
            PreviewText = ex.Message;
        }
    }

    public async Task DownloadAsync(IReadOnlyList<FileRow> rows, string localDirectory)
    {
        foreach (var row in rows)
        {
            try
            {
                await files.DownloadAsync(containerId, row.Path, localDirectory);
            }
            catch (EngineException ex)
            {
                ui.ShowError($"Couldn't download {row.Name}", ex);
                return;
            }
        }

        ui.ShowToast(rows.Count == 1 ? $"Downloaded {rows[0].Name}." : $"Downloaded {rows.Count} items.");
    }

    public async Task UploadAsync(IReadOnlyList<string> localPaths)
    {
        var target = CurrentPath;
        foreach (var path in localPaths)
        {
            try
            {
                await files.UploadAsync(containerId, path, target);
            }
            catch (EngineException ex)
            {
                ui.ShowError($"Couldn't upload {System.IO.Path.GetFileName(path)}", ex);
                break;
            }
        }

        ui.ShowToast(localPaths.Count == 1 ? $"Uploaded {System.IO.Path.GetFileName(localPaths[0])} to {target}." : $"Uploaded {localPaths.Count} items to {target}.");
        await LoadAsync(target);
    }

    internal static string DescribeContent(byte[] bytes, long totalSize)
    {
        if (Array.IndexOf(bytes, (byte)0) >= 0)
        {
            return $"Binary file ({Formatting.Bytes(Math.Max(totalSize, bytes.Length))}). Download it to open it.";
        }

        var text = new UTF8Encoding(false, throwOnInvalidBytes: false).GetString(bytes);
        return totalSize > bytes.Length ? text + $"\n\n[preview shows the first {Formatting.Bytes(bytes.Length)} of {Formatting.Bytes(totalSize)}]" : text;
    }

    private async Task<bool> LoadAsync(string path, bool quietIfNotDirectory = false)
    {
        IsLoading = true;
        Error = null;
        try
        {
            IReadOnlyList<ContainerFileEntry>? entries = null;
            if (_snapshot is null && isRunning())
            {
                try
                {
                    entries = await files.ListDirectoryAsync(containerId, path);
                }
                catch (EngineException ex) when (ex.Kind is EngineErrorKind.NotSupported or EngineErrorKind.NotRunning)
                {
                    _snapshotForced = ex.Kind == EngineErrorKind.NotSupported;
                }
            }

            if (entries is null)
            {
                if (_snapshot is null)
                {
                    LoadingText = "Reading the container's filesystem…";
                    try
                    {
                        _snapshot = await files.OpenSnapshotAsync(containerId);
                    }
                    finally
                    {
                        LoadingText = null;
                    }

                    OnPropertyChanged(nameof(IsSnapshot));
                    OnPropertyChanged(nameof(ModeText));
                }

                entries = _snapshot.List(path)
                    ?? throw new EngineException(EngineErrorKind.NotFound, $"{path} doesn't exist or isn't a folder in the snapshot.");
            }

            CurrentPath = path;
            Entries.Clear();
            foreach (var entry in entries.OrderBy(e => e.Kind != ContainerFileKind.Directory).ThenBy(e => e.Name, StringComparer.Ordinal))
            {
                Entries.Add(new FileRow(entry));
            }

            OnPropertyChanged(nameof(CanUpload));
            return true;
        }
        catch (EngineException ex)
        {
            if (!(quietIfNotDirectory && ex.Kind is EngineErrorKind.InvalidArgument or EngineErrorKind.NotFound))
            {
                Error = ex.Message;
            }

            return false;
        }
        finally
        {
            IsLoading = false;
        }
    }

    private static string ParentOf(string path)
    {
        var slash = path.TrimEnd('/').LastIndexOf('/');
        return slash <= 0 ? "/" : path[..slash];
    }

    private static string CombinePath(string directory, string relative) => directory.TrimEnd('/') + "/" + relative;

    /// <summary>Makes an absolute path without ".", "..", duplicate or trailing slashes.</summary>
    internal static string NormalizePath(string path)
    {
        var parts = new List<string>();
        foreach (var part in path.Replace('\\', '/').Split('/', StringSplitOptions.RemoveEmptyEntries))
        {
            if (part == "..")
            {
                if (parts.Count > 0)
                {
                    parts.RemoveAt(parts.Count - 1);
                }
            }
            else if (part != ".")
            {
                parts.Add(part);
            }
        }

        return "/" + string.Join('/', parts);
    }
}
