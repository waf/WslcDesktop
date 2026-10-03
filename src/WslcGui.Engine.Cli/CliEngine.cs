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
        _cli = new WslcCli(options ?? new WslcCliOptions());
        _cli.CommandCompleted += result => CommandCompleted?.Invoke(
            new CliCommandTrace(result.Arguments, result.ExitCode, result.Duration, result.StdErr));
        Info = new CliEngineInfo(_cli);
    }

    public IEngineInfo Info { get; }

    /// <summary>Raised on a background thread after each wslc command completes (for diagnostics views).</summary>
    public event Action<CliCommandTrace>? CommandCompleted;

    public void Dispose() => _cli.Dispose();
}

public sealed record CliCommandTrace(IReadOnlyList<string> Arguments, int ExitCode, TimeSpan Duration, string StdErr);
