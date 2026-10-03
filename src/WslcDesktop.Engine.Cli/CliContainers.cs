using System.Globalization;

namespace WslcDesktop.Engine.Cli;

internal sealed class CliContainerQueries(ICliRunner cli) : IContainerQueries
{
    public async Task<IReadOnlyList<ContainerSummary>> ListAsync(bool includeStopped, CancellationToken cancellationToken = default)
    {
        List<string> arguments = ["container", "list", "--no-trunc", "--format", "json"];
        if (includeStopped)
        {
            arguments.Add("--all");
        }

        var result = await cli.RunCheckedAsync(arguments, cancellationToken).ConfigureAwait(false);
        return CliFormats.ParseJsonLines(result.StdOut, CliJsonContext.Default.ContainerListDto)
            .Where(dto => !string.IsNullOrEmpty(dto.ID))
            .Select(ToSummary)
            .ToList();
    }

    public async Task<string> InspectAsync(string containerId, CancellationToken cancellationToken = default)
    {
        var result = await cli.RunCheckedAsync(["container", "inspect", containerId], cancellationToken).ConfigureAwait(false);
        return result.StdOut;
    }

    internal static ContainerSummary ToSummary(ContainerListDto dto) => new(
        Id: dto.ID!,
        Name: dto.Names ?? string.Empty,
        Image: dto.Image ?? string.Empty,
        Command: CliFormats.UnquoteCommand(dto.Command),
        State: CliFormats.ParseState(dto.State),
        Status: dto.Status ?? string.Empty,
        CreatedAt: CliFormats.ParseListTimestamp(dto.CreatedAt),
        Ports: CliFormats.ParsePorts(dto.Ports),
        Mounts: string.IsNullOrEmpty(dto.Mounts) ? [] : dto.Mounts.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
}

internal sealed class CliContainerLifecycle(ICliRunner cli) : IContainerLifecycle
{
    public Task<string> RunAsync(RunSpec spec, CancellationToken cancellationToken = default) =>
        CreateOrRunAsync("run", spec, cancellationToken);

    public Task<string> CreateAsync(RunSpec spec, CancellationToken cancellationToken = default) =>
        CreateOrRunAsync("create", spec, cancellationToken);

    public Task StartAsync(string containerId, CancellationToken cancellationToken = default) =>
        cli.RunCheckedAsync(["container", "start", containerId], cancellationToken);

    public Task StopAsync(string containerId, TimeSpan? timeout = null, CancellationToken cancellationToken = default)
    {
        List<string> arguments = ["container", "stop"];
        if (timeout is { } t)
        {
            arguments.AddRange(["--time", ((int)Math.Ceiling(t.TotalSeconds)).ToString(CultureInfo.InvariantCulture)]);
        }

        arguments.Add(containerId);
        return cli.RunCheckedAsync(arguments, cancellationToken);
    }

    public Task RestartAsync(string containerId, CancellationToken cancellationToken = default) =>
        cli.RunCheckedAsync(["container", "restart", containerId], cancellationToken);

    public Task KillAsync(string containerId, CancellationToken cancellationToken = default) =>
        cli.RunCheckedAsync(["container", "kill", containerId], cancellationToken);

    public Task RemoveAsync(string containerId, bool force, CancellationToken cancellationToken = default) =>
        cli.RunCheckedAsync(force ? ["container", "remove", "--force", containerId] : ["container", "remove", containerId], cancellationToken);

    public Task PruneAsync(CancellationToken cancellationToken = default) =>
        cli.RunCheckedAsync(["container", "prune", "--force"], cancellationToken);

    private async Task<string> CreateOrRunAsync(string verb, RunSpec spec, CancellationToken cancellationToken)
    {
        var result = await cli.RunCheckedAsync(BuildCreateArguments(verb, spec), cancellationToken).ConfigureAwait(false);

        // Both print the new container's ID as the last line of stdout.
        return result.StdOut.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).LastOrDefault()
            ?? throw new EngineException(EngineErrorKind.Unknown, $"wslc {verb} did not print a container ID.") { Detail = result.StdOut };
    }

    internal static List<string> BuildCreateArguments(string verb, RunSpec spec)
    {
        List<string> arguments = ["container", verb];
        if (verb == "run")
        {
            arguments.Add("--detach");
        }

        if (spec.Name is { Length: > 0 } name)
        {
            arguments.AddRange(["--name", name]);
        }

        foreach (var (key, value) in spec.Environment)
        {
            arguments.AddRange(["--env", $"{key}={value}"]);
        }

        foreach (var port in spec.Ports)
        {
            var host = port.HostIp is { } ip
                ? $"{(ip.Contains(':', StringComparison.Ordinal) ? $"[{ip}]" : ip)}:{port.HostPort}:"
                : port.HostPort is { } hostPort ? $"{hostPort}:" : string.Empty;
            var protocol = port.Protocol == PortProtocol.Udp ? "/udp" : string.Empty;
            arguments.AddRange(["--publish", $"{host}{port.ContainerPort}{protocol}"]);
        }

        foreach (var mount in spec.Mounts)
        {
            arguments.AddRange(["--volume", mount.ReadOnly ? $"{mount.Source}:{mount.Target}:ro" : $"{mount.Source}:{mount.Target}"]);
        }

        if (spec.Network is { Length: > 0 } network)
        {
            arguments.AddRange(["--network", network]);
        }

        if (spec.WorkingDirectory is { Length: > 0 } workingDirectory)
        {
            arguments.AddRange(["--workdir", workingDirectory]);
        }

        if (spec.User is { Length: > 0 } user)
        {
            arguments.AddRange(["--user", user]);
        }

        if (spec.AutoRemove)
        {
            arguments.Add("--rm");
        }

        arguments.Add(spec.Image);
        arguments.AddRange(spec.Command);
        return arguments;
    }
}
