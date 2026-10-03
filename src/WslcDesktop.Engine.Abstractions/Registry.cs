namespace WslcDesktop.Engine;

public interface IRegistryService
{
    /// <param name="server">Null for the default registry (Docker Hub).</param>
    Task LoginAsync(string? server, string username, string password, CancellationToken cancellationToken = default);

    Task LogoutAsync(string? server, CancellationToken cancellationToken = default);
}

/// <summary>Engine-level maintenance used by the Settings and Troubleshoot pages.</summary>
public interface IEngineMaintenance
{
    /// <summary>Version, kernel, settings file, sessions and so on, for display. Must not start the engine VM.</summary>
    Task<IReadOnlyList<KeyValuePair<string, string>>> GetDetailsAsync(CancellationToken cancellationToken = default);

    /// <summary>Opens the engine's own settings file in the user's editor.</summary>
    void OpenSettingsFile();

    /// <summary>Stops the engine session (all running containers stop); it starts again on the next request.</summary>
    Task RestartEngineAsync(CancellationToken cancellationToken = default);
}
