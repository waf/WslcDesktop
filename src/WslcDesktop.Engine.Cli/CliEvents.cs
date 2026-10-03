using System.Globalization;
using System.Runtime.CompilerServices;
using System.Text.Json;

namespace WslcDesktop.Engine.Cli;

/// <summary>
/// Streams <c>wslc events</c>. Note that an open events stream keeps the engine VM alive (S1 §4), so callers should
/// only watch while containers are running.
/// </summary>
internal sealed class CliEventSource(ICliRunner cli) : IEventSource
{
    public async IAsyncEnumerable<EngineEvent> WatchAsync([EnumeratorCancellation] CancellationToken cancellationToken)
    {
        await foreach (var line in cli.StreamLinesAsync(["events"], cancellationToken).ConfigureAwait(false))
        {
            if (Parse(line) is { } engineEvent)
            {
                yield return engineEvent;
            }
        }
    }

    /// <summary>
    /// Parses one event line. WSL 3.0.1 prints text:
    /// <c>2026-10-03T10:37:33.000000000+07:00 container start &lt;id&gt; (image=postgres:16-alpine, name=pg)</c>.
    /// Later versions add <c>--format json</c> with Docker-shaped objects, which are accepted too.
    /// </summary>
    internal static EngineEvent? Parse(string line)
    {
        line = line.Trim();
        if (line.Length == 0)
        {
            return null;
        }

        return line[0] == '{' ? ParseJson(line) : ParseText(line);
    }

    private static EngineEvent? ParseText(string line)
    {
        var parts = line.Split(' ', 4, StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length < 4 || !DateTimeOffset.TryParse(parts[0], CultureInfo.InvariantCulture, DateTimeStyles.None, out var time))
        {
            return null;
        }

        // parts[3] = "<id> (k=v, k=v)" or just "<id>".
        var rest = parts[3];
        var open = rest.IndexOf(" (", StringComparison.Ordinal);
        var id = open >= 0 ? rest[..open] : rest;
        var attributes = new Dictionary<string, string>(StringComparer.Ordinal);
        if (open >= 0 && rest.EndsWith(')'))
        {
            // Values containing ", " are ambiguous in this format; that only affects unusual label values.
            foreach (var pair in rest[(open + 2)..^1].Split(", ", StringSplitOptions.RemoveEmptyEntries))
            {
                var equals = pair.IndexOf('=', StringComparison.Ordinal);
                if (equals > 0)
                {
                    attributes[pair[..equals]] = pair[(equals + 1)..];
                }
            }
        }

        return new EngineEvent(parts[1], parts[2], id, attributes, time);
    }

    private static EngineEvent? ParseJson(string line)
    {
        try
        {
            var dto = JsonSerializer.Deserialize(line, CliJsonContext.Default.EventDto);
            if (dto?.Type is not { } type || dto.Action is not { } action)
            {
                return null;
            }

            var time = dto.TimeNano is { } nanos
                ? DateTimeOffset.UnixEpoch.AddTicks(nanos / 100)
                : dto.Time is { } seconds ? DateTimeOffset.FromUnixTimeSeconds(seconds) : DateTimeOffset.UtcNow;
            return new EngineEvent(type, action, dto.Actor?.ID ?? string.Empty, dto.Actor?.Attributes ?? [], time);
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
