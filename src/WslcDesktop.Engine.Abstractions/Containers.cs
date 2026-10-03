namespace WslcDesktop.Engine;

public interface IContainerQueries
{
    Task<IReadOnlyList<ContainerSummary>> ListAsync(bool includeStopped, CancellationToken cancellationToken = default);

    /// <summary>Returns the engine's raw inspect document (JSON) for display.</summary>
    Task<string> InspectAsync(string containerId, CancellationToken cancellationToken = default);
}

public interface IContainerLifecycle
{
    /// <summary>Creates and starts a container. Returns its ID.</summary>
    Task<string> RunAsync(RunSpec spec, CancellationToken cancellationToken = default);

    /// <summary>Creates a container without starting it. Returns its ID.</summary>
    Task<string> CreateAsync(RunSpec spec, CancellationToken cancellationToken = default);

    Task StartAsync(string containerId, CancellationToken cancellationToken = default);
    Task StopAsync(string containerId, TimeSpan? timeout = null, CancellationToken cancellationToken = default);
    Task RestartAsync(string containerId, CancellationToken cancellationToken = default);
    Task KillAsync(string containerId, CancellationToken cancellationToken = default);
    Task RemoveAsync(string containerId, bool force, CancellationToken cancellationToken = default);

    /// <summary>Removes all stopped containers.</summary>
    Task PruneAsync(CancellationToken cancellationToken = default);
}

public enum ContainerState
{
    Unknown,
    Created,
    Running,
    Paused,
    Restarting,
    Exited,
    Removing,
    Dead,
}

/// <param name="Status">Human-readable status from the engine, for example "Up 5 minutes".</param>
/// <param name="Mounts">Named volumes and host paths mounted into the container.</param>
public sealed record ContainerSummary(
    string Id,
    string Name,
    string Image,
    string Command,
    ContainerState State,
    string Status,
    DateTimeOffset? CreatedAt,
    IReadOnlyList<PortMapping> Ports,
    IReadOnlyList<string>? Mounts = null);

public enum PortProtocol
{
    Tcp,
    Udp,
}

public sealed record PortMapping(int ContainerPort, int? HostPort, string? HostIp, PortProtocol Protocol);

/// <param name="Source">A Windows path for a bind mount, or a volume name for a named volume.</param>
public sealed record VolumeMount(string Source, string Target, bool ReadOnly);

/// <summary>Everything needed to create or run a container. Engines translate this to their own options.</summary>
public sealed record RunSpec(string Image)
{
    public string? Name { get; init; }

    /// <summary>Command and arguments; empty means the image default.</summary>
    public IReadOnlyList<string> Command { get; init; } = [];

    public IReadOnlyDictionary<string, string> Environment { get; init; } = new Dictionary<string, string>();
    public IReadOnlyList<PortMapping> Ports { get; init; } = [];
    public IReadOnlyList<VolumeMount> Mounts { get; init; } = [];
    public string? Network { get; init; }
    public string? WorkingDirectory { get; init; }
    public string? User { get; init; }
    public bool AutoRemove { get; init; }
}
