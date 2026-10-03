namespace WslcGui.Engine;

public interface ITerminalSessionFactory
{
    /// <summary>Starts an interactive shell (with a TTY) in a running container.</summary>
    Task<ITerminalSession> OpenAsync(string containerId, string? shell, TerminalSize size, CancellationToken cancellationToken = default);
}

public readonly record struct TerminalSize(int Columns, int Rows);

/// <summary>
/// A byte pipe to an interactive TTY. It knows nothing about rendering; a terminal view consumes
/// <see cref="Output"/> and feeds <see cref="Input"/>.
/// </summary>
public interface ITerminalSession : IAsyncDisposable
{
    /// <summary>Bytes typed by the user (UTF-8, VT input sequences).</summary>
    Stream Input { get; }

    /// <summary>Bytes produced by the TTY (UTF-8, VT output sequences).</summary>
    Stream Output { get; }

    void Resize(TerminalSize size);

    /// <summary>Completes with the shell's exit code.</summary>
    Task<int> Completion { get; }
}

public interface IExternalTerminalLauncher
{
    /// <summary>Opens an interactive shell into the container in an external terminal window.</summary>
    void Launch(string containerId, string? shell);
}
