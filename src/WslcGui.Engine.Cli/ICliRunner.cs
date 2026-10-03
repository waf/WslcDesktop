using System.Text.Json;
using System.Text.Json.Serialization.Metadata;

namespace WslcGui.Engine.Cli;

/// <summary>
/// Runs wslc commands. <see cref="WslcCli"/> is the real implementation; tests substitute recorded output.
/// Arguments are passed as a list and never concatenated into a command line by callers.
/// </summary>
internal interface ICliRunner
{
    Task<CliResult> RunAsync(IReadOnlyList<string> arguments, CancellationToken cancellationToken);

    /// <summary>Streams stdout lines of a long-running command (for example <c>logs -f</c> or <c>events</c>) until it exits or is cancelled.</summary>
    IAsyncEnumerable<string> StreamLinesAsync(IReadOnlyList<string> arguments, CancellationToken cancellationToken);
}

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
