namespace WslcGui.Engine.Cli;

/// <summary>
/// Composition entry point for the wslc-CLI-backed engine. The app references this type only from its
/// composition root; everything else uses the interfaces in WslcGui.Engine.
/// </summary>
public sealed class CliEngine : IDisposable
{
    private readonly WslcCli _cli;

    public CliEngine(WslcCliOptions? options = null)
    {
        options ??= new WslcCliOptions();
        _cli = new WslcCli(options);
        _cli.CommandCompleted += result => CommandCompleted?.Invoke(
            new CliCommandTrace(result.Arguments, result.ExitCode, result.Duration, result.StdErr));

        Info = new CliEngineInfo(_cli, options.Session);
        Containers = new CliContainerQueries(_cli);
        Lifecycle = new CliContainerLifecycle(_cli);
        Images = new CliImageService(_cli);
        Events = new CliEventSource(_cli);
        Logs = new CliLogSource(_cli);
        Exec = new CliExecService(_cli);
        ExternalTerminal = new CliExternalTerminal(_cli);
        Files = new CliContainerFiles(_cli);
        Stats = new CliStatsSource(_cli);
        Volumes = new CliVolumeService(_cli);
        Networks = new CliNetworkService(_cli);
        Registry = new CliRegistryService(_cli);
        Maintenance = new CliEngineMaintenance(_cli);
    }

    public IEngineInfo Info { get; }
    public IContainerQueries Containers { get; }
    public IContainerLifecycle Lifecycle { get; }
    public IImageService Images { get; }
    public IEventSource Events { get; }
    public ILogSource Logs { get; }
    public IExecService Exec { get; }
    public IExternalTerminalLauncher ExternalTerminal { get; }
    public IContainerFiles Files { get; }
    public IStatsSource Stats { get; }
    public IVolumeService Volumes { get; }
    public INetworkService Networks { get; }
    public IRegistryService Registry { get; }
    public IEngineMaintenance Maintenance { get; }

    /// <summary>The wslc command line equivalent to running <paramref name="spec"/>, for display ("show CLI command").</summary>
    public static string DescribeRun(RunSpec spec) =>
        CliCommandLine.Format(["wslc", .. CliContainerLifecycle.BuildCreateArguments("run", spec)]);

    /// <summary>Raised on a background thread after each wslc command completes (for diagnostics views).</summary>
    public event Action<CliCommandTrace>? CommandCompleted;

    public void Dispose() => _cli.Dispose();
}

public sealed record CliCommandTrace(IReadOnlyList<string> Arguments, int ExitCode, TimeSpan Duration, string StdErr);
