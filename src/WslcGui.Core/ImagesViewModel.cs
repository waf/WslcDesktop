using WslcGui.Engine;

namespace WslcGui.Core;

public sealed record ImageRow(
    string Id,
    string Reference,
    string Repository,
    string Tag,
    long SizeBytes,
    DateTimeOffset? CreatedAt,
    string CreatedText,
    int? ContainerCount,
    string? Pending)
{
    public string ShortId => Formatting.ShortId(Id);
    public string SizeText => Formatting.Bytes(SizeBytes);
    public bool IsInUse => ContainerCount > 0;
    public string UsageText => Pending ?? (ContainerCount switch
    {
        null => string.Empty,
        0 => "Unused",
        1 => "In use (1 container)",
        var n => $"In use ({n} containers)",
    });

    public static ImageRow From(ImageSummary image, DateTimeOffset now) => new(
        image.Id,
        image.Reference,
        image.Repository ?? "<none>",
        image.Tag ?? "<none>",
        image.SizeBytes,
        image.CreatedAt,
        Formatting.Ago(image.CreatedAt, now),
        image.ContainerCount,
        Pending: null);
}

public sealed class ImagesViewModel(
    IImageService images,
    IEngineInfo engineInfo,
    IUserInteraction ui,
    TimeProvider? timeProvider = null)
    : ResourceListViewModel<ImageRow>(engineInfo, ui, timeProvider)
{
    public async Task RemoveAsync(IReadOnlyList<ImageRow> rows)
    {
        if (rows.Count == 0)
        {
            return;
        }

        var what = rows.Count == 1 ? $"'{rows[0].Reference}'" : $"{rows.Count} images";
        var inUse = rows.Count(r => r.IsInUse);
        var message = inUse == 0
            ? $"Remove {what}?"
            : $"Remove {what}? {inUse} {(inUse == 1 ? "is" : "are")} used by containers and will be removed anyway (force).";
        if (!await Ui.ConfirmAsync("Remove images", message, "Remove"))
        {
            return;
        }

        UpdateRows(row => row with { Pending = "Removing…" }, rows.Select(r => r.Id).ToHashSet());
        await RunForEachAsync(rows, "remove", r => images.RemoveAsync(r.Reference, force: r.IsInUse));
    }

    public async Task TagAsync(ImageRow row, string target)
    {
        try
        {
            await images.TagAsync(row.Reference, target);
            Ui.ShowToast($"Tagged {row.Reference} as {target}.");
        }
        catch (EngineException ex)
        {
            Ui.ShowError($"Couldn't tag {row.Reference}", ex);
        }

        await RefreshAsync();
    }

    /// <summary>Saves images (with tags) into one tar archive.</summary>
    public Task SaveAsync(IReadOnlyList<ImageRow> rows, string tarPath) =>
        RunFileOperationAsync(
            () => images.SaveAsync(rows.Select(r => r.Reference).ToList(), tarPath),
            $"Saved {(rows.Count == 1 ? rows[0].Reference : $"{rows.Count} images")} to {Path.GetFileName(tarPath)}.",
            "Couldn't save the images");

    /// <summary>Loads images from a tar archive made by save.</summary>
    public Task LoadAsync(string tarPath) =>
        RunFileOperationAsync(() => images.LoadAsync(tarPath), $"Loaded images from {Path.GetFileName(tarPath)}.", "Couldn't load the images");

    /// <summary>Creates an image from a root filesystem tarball.</summary>
    public Task ImportAsync(string tarPath, string reference) =>
        RunFileOperationAsync(
            () => images.ImportAsync(tarPath, reference.Trim().Length > 0 ? reference.Trim() : null),
            $"Imported {Path.GetFileName(tarPath)}.",
            "Couldn't import the image");

    /// <param name="all">Remove all images without containers, not just untagged (dangling) ones.</param>
    public async Task PruneAsync(bool all)
    {
        var message = all
            ? "Remove all images that aren't used by a container?"
            : "Remove untagged (dangling) images?";
        if (!await Ui.ConfirmAsync("Remove unused images", message, "Remove"))
        {
            return;
        }

        try
        {
            await images.PruneAsync(all);
            Ui.ShowToast("Removed unused images.");
        }
        catch (EngineException ex)
        {
            Ui.ShowError("Couldn't remove unused images", ex);
        }

        await RefreshAsync();
    }

    private async Task RunFileOperationAsync(Func<Task> operation, string doneText, string errorTitle)
    {
        try
        {
            Ui.ShowToast("Working…");
            await operation();
            Ui.ShowToast(doneText);
        }
        catch (EngineException ex)
        {
            Ui.ShowError(errorTitle, ex);
        }

        await RefreshAsync();
    }

    protected override async Task<IReadOnlyList<ImageRow>> LoadAsync(CancellationToken cancellationToken)
    {
        var list = await images.ListAsync(cancellationToken);
        var now = Time.GetUtcNow();
        return list
            .OrderBy(i => i.Repository is null)
            .ThenBy(i => i.Repository, StringComparer.OrdinalIgnoreCase)
            .ThenBy(i => i.Tag, StringComparer.OrdinalIgnoreCase)
            .Select(i => ImageRow.From(i, now))
            .ToList();
    }

    protected override string KeyOf(ImageRow row) => row.Id + "|" + row.Reference;

    protected override string DisplayName(ImageRow row) => row.Reference;

    protected override bool Matches(ImageRow row, string searchText) =>
        row.Reference.Contains(searchText, StringComparison.OrdinalIgnoreCase)
        || row.ShortId.StartsWith(searchText, StringComparison.OrdinalIgnoreCase);
}
