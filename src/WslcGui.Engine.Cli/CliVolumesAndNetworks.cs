namespace WslcGui.Engine.Cli;

internal sealed class CliVolumeService(ICliRunner cli) : IVolumeService
{
    private const string AnonymousLabel = "com.docker.volume.anonymous";

    public async Task<IReadOnlyList<VolumeSummary>> ListAsync(CancellationToken cancellationToken = default)
    {
        var result = await cli.RunCheckedAsync(["volume", "list", "--format", "json"], cancellationToken).ConfigureAwait(false);
        return CliFormats.ParseJsonLines(result.StdOut, CliJsonContext.Default.VolumeListDto)
            .Where(dto => !string.IsNullOrEmpty(dto.Name))
            .Select(dto => new VolumeSummary(
                dto.Name!,
                dto.Driver ?? string.Empty,
                CliFormats.NoneToNull(dto.Mountpoint),
                dto.Labels?.Contains(AnonymousLabel, StringComparison.Ordinal) == true))
            .ToList();
    }

    public async Task<string> InspectAsync(string name, CancellationToken cancellationToken = default) =>
        (await cli.RunCheckedAsync(["volume", "inspect", name], cancellationToken).ConfigureAwait(false)).StdOut;

    public Task CreateAsync(string name, string? driver = null, CancellationToken cancellationToken = default) =>
        cli.RunCheckedAsync(driver is { Length: > 0 } ? ["volume", "create", "--driver", driver, name] : ["volume", "create", name], cancellationToken);

    public Task RemoveAsync(string name, CancellationToken cancellationToken = default) =>
        cli.RunCheckedAsync(["volume", "remove", name], cancellationToken);

    public Task PruneAsync(bool all, CancellationToken cancellationToken = default) =>
        cli.RunCheckedAsync(all ? ["volume", "prune", "--force", "--all"] : ["volume", "prune", "--force"], cancellationToken);
}

internal sealed class CliNetworkService(ICliRunner cli) : INetworkService
{
    private static readonly HashSet<string> s_builtIn = new(StringComparer.Ordinal) { "bridge", "host", "none" };

    public async Task<IReadOnlyList<NetworkSummary>> ListAsync(CancellationToken cancellationToken = default)
    {
        var result = await cli.RunCheckedAsync(["network", "list", "--no-trunc", "--format", "json"], cancellationToken).ConfigureAwait(false);
        return CliFormats.ParseJsonLines(result.StdOut, CliJsonContext.Default.NetworkListDto)
            .Where(dto => !string.IsNullOrEmpty(dto.Name))
            .Select(dto => new NetworkSummary(
                dto.ID ?? string.Empty,
                dto.Name!,
                dto.Driver ?? string.Empty,
                CliFormats.ParseListTimestamp(dto.CreatedAt),
                dto.Internal == "true",
                dto.IPv6 == "true",
                s_builtIn.Contains(dto.Name!)))
            .ToList();
    }

    public async Task<string> InspectAsync(string name, CancellationToken cancellationToken = default) =>
        (await cli.RunCheckedAsync(["network", "inspect", name], cancellationToken).ConfigureAwait(false)).StdOut;

    public Task CreateAsync(string name, bool isInternal = false, CancellationToken cancellationToken = default) =>
        cli.RunCheckedAsync(isInternal ? ["network", "create", "--internal", name] : ["network", "create", name], cancellationToken);

    public Task RemoveAsync(string name, CancellationToken cancellationToken = default) =>
        cli.RunCheckedAsync(["network", "remove", name], cancellationToken);

    public Task PruneAsync(CancellationToken cancellationToken = default) =>
        cli.RunCheckedAsync(["network", "prune", "--force"], cancellationToken);

    public Task ConnectAsync(string network, string containerId, CancellationToken cancellationToken = default) =>
        cli.RunCheckedAsync(["network", "connect", network, containerId], cancellationToken);

    public Task DisconnectAsync(string network, string containerId, CancellationToken cancellationToken = default) =>
        cli.RunCheckedAsync(["network", "disconnect", network, containerId], cancellationToken);
}
