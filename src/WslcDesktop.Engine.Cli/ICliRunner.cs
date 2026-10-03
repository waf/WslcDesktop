using System.Text.Json;
using System.Text.Json.Serialization.Metadata;

namespace WslcDesktop.Engine.Cli;

/// <summary>
/// Runs wslc commands. <see cref="WslcCli"/> is the real implementation; tests substitute recorded output.
/// Arguments are passed as a list and never concatenated into a command line by callers.
/// </summary>
internal interface ICliRunner
{
    Task<CliResult> RunAsync(IReadOnlyList<string> arguments, CancellationToken cancellationToken);

    /// <summary>Like <see cref="RunAsync"/>, writing <paramref name="standardInput"/> to the command's stdin (for secrets).</summary>
    Task<CliResult> RunWithInputAsync(IReadOnlyList<string> arguments, string standardInput, CancellationToken cancellationToken);

    /// <summary>Streams stdout lines of a long-running command (for example <c>logs -f</c> or <c>events</c>) until it exits or is cancelled.</summary>
    IAsyncEnumerable<string> StreamLinesAsync(IReadOnlyList<string> arguments, CancellationToken cancellationToken);

    /// <summary>
    /// Streams stdout and stderr lines as they arrive (for commands whose stderr is content, such as <c>logs</c>),
    /// ending with one <see cref="CliOutputKind.Exit"/> item that carries the exit code. Never throws for a non-zero exit.
    /// </summary>
    IAsyncEnumerable<CliOutputLine> StreamOutputAsync(IReadOnlyList<string> arguments, CancellationToken cancellationToken);

    /// <summary>Starts a process that the user interacts with directly (a terminal window). It isn't tracked or killed with the app.</summary>
    /// <param name="hidden">Don't show a console window (for helpers that open their own UI).</param>
    void LaunchDetached(string executable, IReadOnlyList<string> arguments, bool hidden = false);

    /// <summary>The resolved wslc.exe path, or null if wslc isn't installed.</summary>
    string? ExecutablePath { get; }

    /// <summary>Global wslc arguments every command gets (for example <c>--session name</c>).</summary>
    IReadOnlyList<string> GlobalArguments { get; }
}

internal enum CliOutputKind
{
    StdOut,
    StdErr,
    Exit,
}

/// <param name="Text">The line (no line terminator); empty for <see cref="CliOutputKind.Exit"/>.</param>
internal readonly record struct CliOutputLine(CliOutputKind Kind, string Text, int ExitCode = 0);

internal sealed record CliResult(IReadOnlyList<string> Arguments, int ExitCode, string StdOut, string StdErr, TimeSpan Duration);

internal static class CliRunnerExtensions
{
    /// <summary>Runs a command and throws <see cref="EngineException"/> if it fails.</summary>
    public static async Task<CliResult> RunCheckedAsync(this ICliRunner runner, IReadOnlyList<string> arguments, CancellationToken cancellationToken)
    {
        var result = await runner.RunAsync(arguments, cancellationToken).ConfigureAwait(false);
        if (result.ExitCode != 0)
        {
            throw CliErrors.FromResult(result);
        }

        return result;
    }

    public static async Task<T> RunJsonAsync<T>(this ICliRunner runner, IReadOnlyList<string> arguments, JsonTypeInfo<T> typeInfo, CancellationToken cancellationToken)
    {
        var result = await runner.RunCheckedAsync(arguments, cancellationToken).ConfigureAwait(false);
        try
        {
            return JsonSerializer.Deserialize(result.StdOut, typeInfo)
                ?? throw new JsonException("Output was the JSON literal null.");
        }
        catch (JsonException ex)
        {
            throw new EngineException(EngineErrorKind.Unknown, $"Unexpected output from 'wslc {string.Join(' ', arguments)}'.", ex)
            {
                Detail = result.StdOut,
            };
        }
    }
}
