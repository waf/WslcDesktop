namespace WslcGui.Engine;

public interface IVolumeService
{
    Task<IReadOnlyList<VolumeSummary>> ListAsync(CancellationToken cancellationToken = default);
    Task<string> InspectAsync(string name, CancellationToken cancellationToken = default);
    Task CreateAsync(string name, CancellationToken cancellationToken = default);
    Task RemoveAsync(string name, CancellationToken cancellationToken = default);
    Task PruneAsync(CancellationToken cancellationToken = default);
}

public sealed record VolumeSummary(string Name, string Driver);

public interface INetworkService
{
    Task<IReadOnlyList<NetworkSummary>> ListAsync(CancellationToken cancellationToken = default);
    Task<string> InspectAsync(string name, CancellationToken cancellationToken = default);
    Task CreateAsync(string name, CancellationToken cancellationToken = default);
    Task RemoveAsync(string name, CancellationToken cancellationToken = default);
    Task PruneAsync(CancellationToken cancellationToken = default);
    Task ConnectAsync(string network, string containerId, CancellationToken cancellationToken = default);
    Task DisconnectAsync(string network, string containerId, CancellationToken cancellationToken = default);
}

public sealed record NetworkSummary(string Id, string Name, string Driver);
