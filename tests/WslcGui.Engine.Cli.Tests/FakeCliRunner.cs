using System.Globalization;
using System.Runtime.CompilerServices;
using System.Text;

namespace WslcGui.Engine.Cli.Tests;

/// <summary>Replays canned output for exact argument lists, and records what was invoked.</summary>
internal sealed class FakeCliRunner : ICliRunner
{
    private readonly Dictionary<string, CliResult> _results = [];

    public List<IReadOnlyList<string>> Invocations { get; } = [];

    public FakeCliRunner Returns(string arguments, string stdout, int exitCode = 0, string stderr = "")
    {
        var argumentList = arguments.Split(' ');
        _results[arguments] = new CliResult(argumentList, exitCode, stdout, stderr, TimeSpan.Zero);
        return this;
    }

    public Task<CliResult> RunAsync(IReadOnlyList<string> arguments, CancellationToken cancellationToken)
    {
        Invocations.Add(arguments);
        var key = string.Join(' ', arguments);
        return _results.TryGetValue(key, out var result)
            ? Task.FromResult(result)
            : throw new InvalidOperationException($"No canned result for 'wslc {key}'.");
    }

    public async IAsyncEnumerable<string> StreamLinesAsync(IReadOnlyList<string> arguments, [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var result = await RunAsync(arguments, cancellationToken);
        foreach (var line in result.StdOut.Split('\n'))
        {
            yield return line.TrimEnd('\r');
        }
    }
}

internal static class Fixtures
{
    public static string Read(string name) => File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "fixtures", "cli", name));

    /// <summary>
    /// Reads a combined capture written by the S1 spike:
    /// <c># argv: …</c>, <c># exit: N</c>, <c># --- stdout (N bytes)</c>, raw stdout, <c># --- stderr (N bytes)</c>, raw stderr.
    /// </summary>
    public static CliResult ReadResult(string name)
    {
        var lines = Read(name).Split('\n');
        var argv = lines.Single(l => l.StartsWith("# argv: ", StringComparison.Ordinal))["# argv: wslc ".Length..].Split(' ');
        var exitCode = int.Parse(lines.Single(l => l.StartsWith("# exit: ", StringComparison.Ordinal))["# exit: ".Length..], CultureInfo.InvariantCulture);

        var stdout = new StringBuilder();
        var stderr = new StringBuilder();
        StringBuilder? current = null;
        foreach (var line in lines)
        {
            if (line.StartsWith("# --- stdout", StringComparison.Ordinal)) { current = stdout; continue; }
            if (line.StartsWith("# --- stderr", StringComparison.Ordinal)) { current = stderr; continue; }
            current?.Append(line).Append('\n');
        }

        return new CliResult(argv, exitCode, stdout.ToString(), stderr.ToString(), TimeSpan.Zero);
    }
}
