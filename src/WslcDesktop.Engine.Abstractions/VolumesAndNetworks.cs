namespace WslcDesktop.Engine;

public interface IVolumeService
{
    Task<IReadOnlyList<VolumeSummary>> ListAsync(CancellationToken cancellationToken = default);
    Task<string> InspectAsync(string name, CancellationToken cancellationToken = default);

    /// <param name="driver">Null for the engine default.</param>
    Task CreateAsync(string name, string? driver = null, CancellationToken cancellationToken = default);

    Task RemoveAsync(string name, CancellationToken cancellationToken = default);

    /// <param name="all">Remove all unused volumes, not just anonymous ones.</param>
    Task PruneAsync(bool all, CancellationToken cancellationToken = default);
}

/// <param name="IsAnonymous">Created implicitly for a container (named by a random ID).</param>
public sealed record VolumeSummary(string Name, string Driver, string? Mountpoint, bool IsAnonymous);

public interface INetworkService
{
    Task<IReadOnlyList<NetworkSummary>> ListAsync(CancellationToken cancellationToken = default);
    Task<string> InspectAsync(string name, CancellationToken cancellationToken = default);
    Task CreateAsync(string name, bool isInternal = false, CancellationToken cancellationToken = default);
    Task RemoveAsync(string name, CancellationToken cancellationToken = default);
    Task PruneAsync(CancellationToken cancellationToken = default);
    Task ConnectAsync(string network, string containerId, CancellationToken cancellationToken = default);
    Task DisconnectAsync(string network, string containerId, CancellationToken cancellationToken = default);
}

/// <param name="IsBuiltIn">One of the engine's predefined networks (bridge, host, none), which can't be removed.</param>
public sealed record NetworkSummary(string Id, string Name, string Driver, DateTimeOffset? CreatedAt, bool IsInternal, bool IsIPv6, bool IsBuiltIn);
