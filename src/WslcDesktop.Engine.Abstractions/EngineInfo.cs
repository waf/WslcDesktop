namespace WslcDesktop.Engine;

public interface IEngineInfo
{
    Task<EngineVersion> GetVersionAsync(CancellationToken cancellationToken = default);

    /// <summary>Checks that the engine can be used at all, without throwing for the expected failure cases.</summary>
    Task<EngineHealth> CheckHealthAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Whether the engine's VM is currently up. Must not start the VM. Callers use this to avoid waking an idle
    /// engine with background polling (the VM idles out ~30 s after the last container stops; a cold start takes seconds).
    /// </summary>
    Task<EngineRuntimeState> GetRuntimeStateAsync(CancellationToken cancellationToken = default);
}

public enum EngineRuntimeState
{
    Unknown,
    Running,
    /// <summary>The VM is down. The next command that needs it starts it (cold start).</summary>
    Idle,
}

public sealed record EngineVersion(string ClientVersion);

public enum EngineHealthStatus
{
    Ready,
    /// <summary>wslc.exe (or the WSL runtime) is not installed.</summary>
    NotInstalled,
    /// <summary>Installed, but a command failed (service not running, version too old, ...).</summary>
    Error,
}

public sealed record EngineHealth(EngineHealthStatus Status, EngineVersion? Version, string? Message);
