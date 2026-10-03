namespace WslcGui.Engine;

public interface IImageService
{
    Task<IReadOnlyList<ImageSummary>> ListAsync(CancellationToken cancellationToken = default);
    Task<string> InspectAsync(string image, CancellationToken cancellationToken = default);
    Task PullAsync(string reference, IProgress<PullProgress>? progress = null, CancellationToken cancellationToken = default);
    Task RemoveAsync(string image, bool force, CancellationToken cancellationToken = default);
    Task TagAsync(string source, string target, CancellationToken cancellationToken = default);
    Task PruneAsync(bool all, CancellationToken cancellationToken = default);

    /// <summary>Builds an image, reporting each line of build output.</summary>
    Task BuildAsync(BuildSpec spec, IProgress<string>? output = null, CancellationToken cancellationToken = default);

    /// <summary>Saves images (with their tags) to a tar archive.</summary>
    Task SaveAsync(IReadOnlyList<string> images, string tarPath, CancellationToken cancellationToken = default);

    /// <summary>Loads images from a tar archive made by save.</summary>
    Task LoadAsync(string tarPath, CancellationToken cancellationToken = default);

    /// <summary>Creates an image from a root filesystem tarball.</summary>
    Task ImportAsync(string tarPath, string? reference, CancellationToken cancellationToken = default);

    Task PushAsync(string reference, IProgress<string>? output = null, CancellationToken cancellationToken = default);
}

/// <param name="ContextDirectory">Local folder sent to the build.</param>
/// <param name="Dockerfile">Path to the Dockerfile; null for the context's Dockerfile.</param>
public sealed record BuildSpec(string ContextDirectory)
{
    public string? Dockerfile { get; init; }
    public string? Tag { get; init; }
    public string? Target { get; init; }
    public IReadOnlyDictionary<string, string> BuildArgs { get; init; } = new Dictionary<string, string>();
    public bool NoCache { get; init; }
    public bool Pull { get; init; }
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
