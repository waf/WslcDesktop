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
/// <param name="SizeBytes">Approximate when the engine only reports a rounded human-readable size.</param>
/// <param name="ContainerCount">Containers (in any state) using the image, if known.</param>
public sealed record ImageSummary(
    string Id,
    string? Repository,
    string? Tag,
    long SizeBytes,
    DateTimeOffset? CreatedAt,
    int? ContainerCount)
{
    /// <summary>"repository:tag", or the short ID for an untagged image. Use this to refer to the image in commands.</summary>
    public string Reference => Repository is null ? Id : Tag is null ? Repository : $"{Repository}:{Tag}";
}

/// <summary>Progress for one layer of a pull.</summary>
/// <param name="LayerId">Null for whole-image status messages.</param>
public sealed record PullProgress(string? LayerId, string Status, long? CurrentBytes, long? TotalBytes);
