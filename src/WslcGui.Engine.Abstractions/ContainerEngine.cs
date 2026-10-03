namespace WslcGui.Engine;

/// <summary>
/// The full set of engine capabilities the app talks to. Each capability is its own interface so that
/// one can be re-implemented (for example over COM) while the others stay on the CLI.
/// </summary>
public sealed record ContainerEngine(
    IEngineInfo Info,
    IContainerQueries Containers,
    IContainerLifecycle Lifecycle,
    IImageService Images,
    IVolumeService Volumes,
    INetworkService Networks,
    IStatsSource Stats,
    IEventSource Events,
    ILogSource Logs,
    IContainerFiles Files,
    IExecService Exec,
    ITerminalSessionFactory Terminals,
    IExternalTerminalLauncher ExternalTerminal);
