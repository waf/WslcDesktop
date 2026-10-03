namespace WslcGui.Engine;

public interface IImageService
{
    Task<IReadOnlyList<ImageSummary>> ListAsync(CancellationToken cancellationToken = default);
    Task<string> InspectAsync(string image, CancellationToken cancellationToken = default);
    Task PullAsync(string reference, IProgress<PullProgress>? progress = null, CancellationToken cancellationToken = default);
    Task RemoveAsync(string image, bool force, CancellationToken cancellationToken = default);
    Task TagAsync(string source, string target, CancellationToken cancellationToken = default);
    Task PruneAsync(bool all, CancellationToken cancellationToken = default);
}

/// <param name="Repository">Repository, or null for an untagged image.</param>
public sealed record ImageSummary(
    string Id,
    string? Repository,
    string? Tag,
    long SizeBytes,
    DateTimeOffset? CreatedAt);

/// <summary>Progress for one layer of a pull.</summary>
/// <param name="LayerId">Null for whole-image status messages.</param>
public sealed record PullProgress(string? LayerId, string Status, long? CurrentBytes, long? TotalBytes);
