using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;

namespace WslcDesktop.Engine.Cli;

/// <summary>Parsers for the Docker-template strings wslc prints in its list output.</summary>
internal static class CliFormats
{
    /// <summary>Parses JSON Lines (one compact object per line, CRLF). Empty output means no items.</summary>
    public static List<T> ParseJsonLines<T>(string output, JsonTypeInfo<T> typeInfo)
    {
        var items = new List<T>();
        foreach (var rawLine in output.Split('\n'))
        {
            var line = rawLine.Trim();
            if (line.Length > 0 && JsonSerializer.Deserialize(line, typeInfo) is { } item)
            {
                items.Add(item);
            }
        }

        return items;
    }

    /// <summary>
    /// Parses Go-style list timestamps: <c>2026-10-03 10:37:36 +0700 GMT+7</c> (containers, images) and
    /// <c>2026-10-03 03:37:32.993374191 +0000 UTC</c> (networks). Anything after the offset is a zone name and ignored.
    /// </summary>
    public static DateTimeOffset? ParseListTimestamp(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var parts = value.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length < 3 || parts[2].Length != 5 || (parts[2][0] != '+' && parts[2][0] != '-'))
        {
            return null;
        }

        // .NET parses at most 7 fractional digits; Go prints up to 9.
        var time = parts[1];
        var dot = time.IndexOf('.', StringComparison.Ordinal);
        if (dot >= 0 && time.Length - dot - 1 > 7)
        {
            time = time[..(dot + 8)];
        }

        // "+0700" -> "+07:00", which "zzz" understands.
        var offset = string.Concat(parts[2].AsSpan(0, 3), ":", parts[2].AsSpan(3));
        return DateTimeOffset.TryParseExact($"{parts[0]} {time} {offset}", ["yyyy-MM-dd HH:mm:ss zzz", "yyyy-MM-dd HH:mm:ss.FFFFFFF zzz"], CultureInfo.InvariantCulture, DateTimeStyles.None, out var result)
            ? result
            : null;
    }

    /// <summary>Parses list <c>Ports</c>: <c>127.0.0.1:15432->5432/tcp, 8080/tcp</c>; empty when none.</summary>
    public static IReadOnlyList<PortMapping> ParsePorts(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return [];
        }

        var ports = new List<PortMapping>();
        foreach (var rawEntry in value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            string? hostIp = null;
            int? hostPort = null;
            var containerPart = rawEntry;

            var arrow = rawEntry.IndexOf("->", StringComparison.Ordinal);
            if (arrow >= 0)
            {
                var hostPart = rawEntry[..arrow];
                containerPart = rawEntry[(arrow + 2)..];

                // IPv6 hosts look like "[::1]:8080"; the port is after the last colon either way.
                var colon = hostPart.LastIndexOf(':');
                if (colon >= 0)
                {
                    hostIp = hostPart[..colon].Trim('[', ']');
                    hostPort = int.TryParse(hostPart[(colon + 1)..], CultureInfo.InvariantCulture, out var hp) ? hp : null;
                }
            }

            var slash = containerPart.IndexOf('/', StringComparison.Ordinal);
            var portText = slash >= 0 ? containerPart[..slash] : containerPart;
            var protocol = slash >= 0 && containerPart[(slash + 1)..].Equals("udp", StringComparison.OrdinalIgnoreCase)
                ? PortProtocol.Udp
                : PortProtocol.Tcp;

            if (int.TryParse(portText, CultureInfo.InvariantCulture, out var containerPort))
            {
                ports.Add(new PortMapping(containerPort, hostPort, string.IsNullOrEmpty(hostIp) ? null : hostIp, protocol));
            }
        }

        return ports;
    }

    /// <summary>
    /// Parses human-readable sizes: decimal (<c>74.8MB</c>, <c>1.25kB</c>, <c>0B</c>) and binary (<c>107.5MiB</c>).
    /// For list sizes like <c>63B (virtual 294MB)</c> only the first value is read.
    /// </summary>
    public static long? ParseSize(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var text = value.Trim();
        var space = text.IndexOf(' ', StringComparison.Ordinal);
        if (space > 0)
        {
            text = text[..space];
        }

        var unitStart = 0;
        while (unitStart < text.Length && (char.IsDigit(text[unitStart]) || text[unitStart] == '.'))
        {
            unitStart++;
        }

        if (!double.TryParse(text.AsSpan(0, unitStart), NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var number))
        {
            return null;
        }

        double? multiplier = text[unitStart..].ToUpperInvariant() switch
        {
            "B" or "" => 1,
            "KB" => 1e3,
            "MB" => 1e6,
            "GB" => 1e9,
            "TB" => 1e12,
            "KIB" => 1L << 10,
            "MIB" => 1L << 20,
            "GIB" => 1L << 30,
            "TIB" => 1L << 40,
            _ => null,
        };

        return multiplier is { } m ? (long)Math.Round(number * m) : null;
    }

    public static ContainerState ParseState(string? value) => value?.ToUpperInvariant() switch
    {
        "CREATED" => ContainerState.Created,
        "RUNNING" => ContainerState.Running,
        "PAUSED" => ContainerState.Paused,
        "RESTARTING" => ContainerState.Restarting,
        "EXITED" => ContainerState.Exited,
        "REMOVING" => ContainerState.Removing,
        "DEAD" => ContainerState.Dead,
        _ => ContainerState.Unknown,
    };

    /// <summary>List output quotes the command (<c>"sleep infinity"</c>); strip the outer quotes.</summary>
    public static string UnquoteCommand(string? value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return string.Empty;
        }

        return value.Length >= 2 && value[0] == '"' && value[^1] == '"' ? value[1..^1] : value;
    }

    /// <summary>wslc prints <c>&lt;none&gt;</c> for missing values (untagged images, digests).</summary>
    public static string? NoneToNull(string? value) =>
        string.IsNullOrEmpty(value) || value == "<none>" || value == "N/A" ? null : value;
}
