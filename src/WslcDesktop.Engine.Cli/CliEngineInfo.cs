using System.Diagnostics;

namespace WslcDesktop.Engine.Cli;

internal sealed class CliEngineInfo(ICliRunner cli, string? session) : IEngineInfo
{
    // The session VM shows up as a "vmmem<session name>" process. The CLI's default session is "wslc-cli-<user>",
    // or "wslc-cli-admin-<user>" when elevated.
    private const string VmProcessPrefix = "vmmem";

    public async Task<EngineVersion> GetVersionAsync(CancellationToken cancellationToken = default)
    {
        var version = await cli.RunJsonAsync(["version", "--format", "json"], CliJsonContext.Default.VersionDto, cancellationToken).ConfigureAwait(false);
        return new EngineVersion(version.Client?.Version ?? "unknown");
    }

    public async Task<EngineHealth> CheckHealthAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var version = await GetVersionAsync(cancellationToken).ConfigureAwait(false);
            return new EngineHealth(EngineHealthStatus.Ready, version, null);
        }
        catch (EngineException ex) when (ex.Kind == EngineErrorKind.Unavailable)
        {
            return new EngineHealth(EngineHealthStatus.NotInstalled, null, ex.Message);
        }
        catch (EngineException ex)
        {
            return new EngineHealth(EngineHealthStatus.Error, null, ex.Message);
        }
    }

    /// <summary>
    /// wslc has no command that reports VM state without booting it, so look for the VM's memory process instead.
    /// This is a heuristic based on WSL 3.0.1 behavior (S1 §4); it returns Unknown if it can't tell.
    /// </summary>
    public Task<EngineRuntimeState> GetRuntimeStateAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            string[] expected = session is null
                ? [$"{VmProcessPrefix}wslc-cli-{Environment.UserName}", $"{VmProcessPrefix}wslc-cli-admin-{Environment.UserName}"]
                : [VmProcessPrefix + session];
            var processes = Process.GetProcesses();
            try
            {
                var running = processes.Any(p => expected.Contains(p.ProcessName, StringComparer.OrdinalIgnoreCase));
                return Task.FromResult(running ? EngineRuntimeState.Running : EngineRuntimeState.Idle);
            }
            finally
            {
                foreach (var process in processes)
                {
                    process.Dispose();
                }
            }
        }
        catch (InvalidOperationException)
        {
            return Task.FromResult(EngineRuntimeState.Unknown);
        }
    }
}
