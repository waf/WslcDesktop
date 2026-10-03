namespace WslcGui.Engine;

public interface IEngineInfo
{
    Task<EngineVersion> GetVersionAsync(CancellationToken cancellationToken = default);

    /// <summary>Checks that the engine can be used at all, without throwing for the expected failure cases.</summary>
    Task<EngineHealth> CheckHealthAsync(CancellationToken cancellationToken = default);
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
