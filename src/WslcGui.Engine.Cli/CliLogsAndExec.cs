using System.Globalization;
using System.Runtime.CompilerServices;

namespace WslcGui.Engine.Cli;

/// <summary>
/// <c>wslc logs -t</c>. Container stdout and stderr come back on wslc's stdout and stderr (S1 §2.10). Every container
/// line has the <c>-t</c> timestamp prefix, which tells it apart from wslc's own error messages on stderr.
/// </summary>
internal sealed class CliLogSource(ICliRunner cli) : ILogSource
{
    public async IAsyncEnumerable<LogLine> ReadAsync(string containerId, LogOptions options, [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        List<string> arguments = ["container", "logs", "--timestamps"];
        if (options.Follow)
        {
            arguments.Add("--follow");
        }

        // wslc rejects "-n 0"; no tail means all lines.
        if (options.Tail is > 0 and var tail)
        {
            arguments.AddRange(["--tail", tail.ToString(CultureInfo.InvariantCulture)]);
        }

        arguments.Add(containerId);

        var errors = new List<string>();
        await foreach (var line in cli.StreamOutputAsync(arguments, cancellationToken).ConfigureAwait(false))
        {
            switch (line.Kind)
            {
                case CliOutputKind.Exit when line.ExitCode != 0:
                    throw CliErrors.FromResult(new CliResult(arguments, line.ExitCode, string.Empty, string.Join('\n', errors), TimeSpan.Zero));
                case CliOutputKind.Exit:
                    yield break;
            }

            if (Parse(line.Text, line.Kind == CliOutputKind.StdErr ? LogOrigin.StdErr : LogOrigin.StdOut) is { } logLine)
            {
                yield return logLine;
            }
            else if (line.Kind == CliOutputKind.StdErr)
            {
                errors.Add(line.Text);
            }
        }
    }

    /// <summary>Parses <c>2026-10-03T03:39:17.395973225Z text</c>. Returns null for lines without the timestamp prefix.</summary>
    internal static LogLine? Parse(string line, LogOrigin origin)
    {
        var space = line.IndexOf(' ', StringComparison.Ordinal);
        var stamp = space < 0 ? line : line[..space];
        if (!TryParseTimestamp(stamp, out var timestamp))
        {
            return null;
        }

        return new LogLine(space < 0 ? string.Empty : line[(space + 1)..], timestamp, origin);
    }

    /// <summary>RFC 3339 with nanoseconds; .NET parses at most 7 fractional digits, so the rest are dropped.</summary>
    private static bool TryParseTimestamp(string text, out DateTimeOffset timestamp)
    {
        timestamp = default;
        if (text.Length < 20 || text[4] != '-' || text[10] != 'T')
        {
            return false;
        }

        var dot = text.IndexOf('.', StringComparison.Ordinal);
        var zone = text.IndexOfAny(['Z', '+', '-'], 19);
        if (dot > 0 && zone > dot + 8)
        {
            text = string.Concat(text.AsSpan(0, dot + 8), text.AsSpan(zone));
        }

        return DateTimeOffset.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out timestamp);
    }
}

internal sealed class CliExecService(ICliRunner cli) : IExecService
{
    public async Task<ExecResult> ExecAsync(string containerId, IReadOnlyList<string> command, CancellationToken cancellationToken = default)
    {
        var result = await cli.RunAsync(["container", "exec", containerId, .. command], cancellationToken).ConfigureAwait(false);

        // The exit code is the command's own, so only treat wslc's coded errors (container not running, not found) as failures.
        if (result.ExitCode != 0 && result.StdErr.Contains("Error code: ", StringComparison.Ordinal))
        {
            throw CliErrors.FromResult(result);
        }

        return new ExecResult(result.ExitCode, result.StdOut, result.StdErr);
    }
}

/// <summary>Opens <c>wslc exec -it</c> in Windows Terminal, or in a new console window if Windows Terminal isn't installed.</summary>
internal sealed class CliExternalTerminal(ICliRunner cli, Func<string?>? findWindowsTerminal = null) : IExternalTerminalLauncher
{
    /// <summary>Prefer bash when the image has it. No ';' because Windows Terminal treats it as a command separator.</summary>
    internal const string DefaultShellScript = "command -v bash >/dev/null && exec bash || exec sh";

    public void Launch(string containerId, string? shell)
    {
        var wslc = cli.ExecutablePath
            ?? throw new EngineException(EngineErrorKind.Unavailable, "wslc.exe was not found. Install or update WSL (wsl --update).");

        List<string> command = [.. cli.GlobalArguments, "container", "exec", "--interactive", "--tty", containerId];
        command.AddRange(shell is { Length: > 0 } ? [shell] : ["sh", "-c", DefaultShellScript]);

        if ((findWindowsTerminal ?? FindWindowsTerminal)() is { } wt)
        {
            cli.LaunchDetached(wt, ["new-tab", "--title", TabTitle(containerId), wslc, .. command]);
        }
        else
        {
            cli.LaunchDetached(wslc, command);
        }
    }

    /// <summary>The container name as given, or the short form of a full 64-character ID.</summary>
    private static string TabTitle(string containerId) =>
        containerId.Length == 64 && containerId.All(char.IsAsciiHexDigitLower) ? containerId[..12] : containerId;

    private static string? FindWindowsTerminal()
    {
        var appAlias = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Microsoft", "WindowsApps", "wt.exe");
        return File.Exists(appAlias) ? appAlias : null;
    }
}
