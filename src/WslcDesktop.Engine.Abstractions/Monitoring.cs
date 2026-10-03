namespace WslcDesktop.Engine;

public interface IStatsSource
{
    /// <summary>One snapshot of resource usage. An empty <paramref name="containerIds"/> means all running containers.</summary>
    Task<IReadOnlyList<ContainerStats>> SnapshotAsync(IReadOnlyList<string> containerIds, CancellationToken cancellationToken = default);
}

public sealed record ContainerStats(
    string ContainerId,
    string Name,
    double CpuPercent,
    long MemoryUsageBytes,
    long MemoryLimitBytes,
    long NetworkRxBytes,
    long NetworkTxBytes,
    long BlockReadBytes,
    long BlockWriteBytes,
    int Pids);

public interface IEventSource
{
    /// <summary>Streams engine events until cancelled. Completes (or throws) if the underlying stream ends.</summary>
    IAsyncEnumerable<EngineEvent> WatchAsync(CancellationToken cancellationToken);
}

/// <param name="Type">Object type, for example "container", "image", "volume", "network".</param>
/// <param name="Action">What happened, for example "start", "die", "destroy".</param>
public sealed record EngineEvent(
    string Type,
    string Action,
    string ActorId,
    IReadOnlyDictionary<string, string> Attributes,
    DateTimeOffset Time);

public interface ILogSource
{
    IAsyncEnumerable<LogLine> ReadAsync(string containerId, LogOptions options, CancellationToken cancellationToken);
}

public sealed record LogOptions(bool Follow = true, int? Tail = 1000, bool Timestamps = true);

public enum LogOrigin
{
    Unknown,
    StdOut,
    StdErr,
}

public sealed record LogLine(string Text, DateTimeOffset? Timestamp, LogOrigin Origin);
