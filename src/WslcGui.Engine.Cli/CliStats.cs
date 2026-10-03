using System.Globalization;

namespace WslcGui.Engine.Cli;

/// <summary><c>container stats --format json</c>: one snapshot, one JSON line per running container (1–2 s per call, S1).</summary>
internal sealed class CliStatsSource(ICliRunner cli) : IStatsSource
{
    public async Task<IReadOnlyList<ContainerStats>> SnapshotAsync(IReadOnlyList<string> containerIds, CancellationToken cancellationToken = default)
    {
        var result = await cli.RunCheckedAsync(["container", "stats", "--format", "json", .. containerIds], cancellationToken).ConfigureAwait(false);
        return CliFormats.ParseJsonLines(result.StdOut, CliJsonContext.Default.StatsDto)
            .Where(dto => !string.IsNullOrEmpty(dto.ID))
            .Select(ToStats)
            .ToList();
    }

    internal static ContainerStats ToStats(StatsDto dto)
    {
        var (memoryUsed, memoryLimit) = SplitPair(dto.MemUsage);
        var (rx, tx) = SplitPair(dto.NetIO);
        var (read, write) = SplitPair(dto.BlockIO);
        return new ContainerStats(
            dto.ID!,
            dto.Name ?? string.Empty,
            ParsePercent(dto.CPUPerc),
            memoryUsed,
            memoryLimit,
            rx,
            tx,
            read,
            write,
            dto.PIDs ?? 0);
    }

    private static double ParsePercent(string? text) =>
        double.TryParse(text?.TrimEnd('%'), NumberStyles.Float, CultureInfo.InvariantCulture, out var value) ? value : 0;

    /// <summary>"107.5MiB / 15.31GiB" -> (bytes, bytes).</summary>
    private static (long, long) SplitPair(string? text)
    {
        var parts = (text ?? string.Empty).Split('/', 2, StringSplitOptions.TrimEntries);
        return (CliFormats.ParseSize(parts[0]) ?? 0, parts.Length > 1 ? CliFormats.ParseSize(parts[1]) ?? 0 : 0);
    }
}
