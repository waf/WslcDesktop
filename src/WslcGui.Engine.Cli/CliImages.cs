using System.Globalization;

namespace WslcGui.Engine.Cli;

internal sealed class CliImageService(ICliRunner cli) : IImageService
{
    public async Task<IReadOnlyList<ImageSummary>> ListAsync(CancellationToken cancellationToken = default)
    {
        var result = await cli.RunCheckedAsync(["image", "list", "--no-trunc", "--format", "json"], cancellationToken).ConfigureAwait(false);
        return CliFormats.ParseJsonLines(result.StdOut, CliJsonContext.Default.ImageListDto)
            .Where(dto => !string.IsNullOrEmpty(dto.ID))
            .Select(ToSummary)
            .ToList();
    }

    public async Task<string> InspectAsync(string image, CancellationToken cancellationToken = default)
    {
        var result = await cli.RunCheckedAsync(["image", "inspect", image], cancellationToken).ConfigureAwait(false);
        return result.StdOut;
    }

    /// <remarks>
    /// wslc doesn't flush pull progress when stdout is redirected (S1 §2.10), so no progress is reported yet.
    /// TODO(M2+): run under ConPTY and parse the progress lines.
    /// </remarks>
    public Task PullAsync(string reference, IProgress<PullProgress>? progress = null, CancellationToken cancellationToken = default) =>
        cli.RunCheckedAsync(["image", "pull", reference], cancellationToken);

    public Task RemoveAsync(string image, bool force, CancellationToken cancellationToken = default) =>
        cli.RunCheckedAsync(force ? ["image", "remove", "--force", image] : ["image", "remove", image], cancellationToken);

    public Task TagAsync(string source, string target, CancellationToken cancellationToken = default) =>
        cli.RunCheckedAsync(["image", "tag", source, target], cancellationToken);

    public Task PruneAsync(bool all, CancellationToken cancellationToken = default) =>
        cli.RunCheckedAsync(all ? ["image", "prune", "--force", "--all"] : ["image", "prune", "--force"], cancellationToken);

    internal static ImageSummary ToSummary(ImageListDto dto)
    {
        var repository = CliFormats.NoneToNull(dto.Repository);
        return new ImageSummary(
            Id: dto.ID!,
            Repository: repository,
            Tag: repository is null ? null : CliFormats.NoneToNull(dto.Tag),
            SizeBytes: CliFormats.ParseSize(dto.Size) ?? 0,
            CreatedAt: CliFormats.ParseListTimestamp(dto.CreatedAt),
            ContainerCount: int.TryParse(dto.Containers, CultureInfo.InvariantCulture, out var count) ? count : null);
    }
}
