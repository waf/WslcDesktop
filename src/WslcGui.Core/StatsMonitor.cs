using WslcGui.Engine;

namespace WslcGui.Core;

public sealed record StatsSample(
    DateTimeOffset Time,
    double CpuPercent,
    long MemoryBytes,
    long MemoryLimitBytes,
    long NetworkRxBytes,
    long NetworkTxBytes,
    long BlockReadBytes,
    long BlockWriteBytes,
    int Pids);

/// <summary>
/// Samples container resource usage for whoever is watching. One non-overlapping loop for all containers (a
/// stats call takes 1–2 s). It only samples while containers are running and the engine VM is up, because any
/// engine call would otherwise keep (or start) the VM.
/// </summary>
public sealed class StatsMonitor(IStatsSource stats, IEngineInfo engineInfo, Func<bool> anyContainerRunning, TimeProvider? timeProvider = null) : IDisposable
{
    public static readonly TimeSpan Interval = TimeSpan.FromSeconds(2);

    /// <summary>Samples kept per container (about two minutes at <see cref="Interval"/> plus call time).</summary>
    public const int HistoryLength = 60;

    private readonly TimeProvider _time = timeProvider ?? TimeProvider.System;
    private readonly Dictionary<string, List<StatsSample>> _history = new(StringComparer.Ordinal);
    private int _watchers;
    private CancellationTokenSource? _loop;

    /// <summary>Raised on the UI thread after each sample.</summary>
    public event Action? Updated;

    /// <summary>The latest sample per running container.</summary>
    public IReadOnlyDictionary<string, StatsSample> Latest { get; private set; } = new Dictionary<string, StatsSample>();

    public IReadOnlyList<StatsSample> History(string containerId) =>
        _history.TryGetValue(containerId, out var samples) ? samples : [];

    /// <summary>Starts sampling until the returned handle is disposed (reference counted).</summary>
    public IDisposable Watch()
    {
        _watchers++;
        if (_loop is null)
        {
            _loop = new CancellationTokenSource();
            _ = RunAsync(_loop.Token);
        }

        return new Watcher(this);
    }

    public void Dispose()
    {
        _loop?.Cancel();
        _loop?.Dispose();
        _loop = null;
    }

    /// <summary>Takes one sample now if conditions allow. Exposed for tests.</summary>
    internal async Task SampleAsync(CancellationToken cancellationToken)
    {
        if (!anyContainerRunning() || await engineInfo.GetRuntimeStateAsync(cancellationToken) == EngineRuntimeState.Idle)
        {
            if (Latest.Count > 0)
            {
                Latest = new Dictionary<string, StatsSample>();
                Updated?.Invoke();
            }

            return;
        }

        IReadOnlyList<ContainerStats> snapshot;
        try
        {
            snapshot = await stats.SnapshotAsync([], cancellationToken);
        }
        catch (EngineException)
        {
            return;
        }

        var now = _time.GetUtcNow();
        var latest = new Dictionary<string, StatsSample>(StringComparer.Ordinal);
        foreach (var s in snapshot)
        {
            var sample = new StatsSample(now, s.CpuPercent, s.MemoryUsageBytes, s.MemoryLimitBytes, s.NetworkRxBytes, s.NetworkTxBytes, s.BlockReadBytes, s.BlockWriteBytes, s.Pids);
            latest[s.ContainerId] = sample;
            if (!_history.TryGetValue(s.ContainerId, out var history))
            {
                _history[s.ContainerId] = history = [];
            }

            history.Add(sample);
            if (history.Count > HistoryLength)
            {
                history.RemoveAt(0);
            }
        }

        Latest = latest;
        Updated?.Invoke();
    }

    private async Task RunAsync(CancellationToken cancellationToken)
    {
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                await SampleAsync(cancellationToken);
                await Task.Delay(Interval, _time, cancellationToken);
            }
        }
        catch (OperationCanceledException)
        {
        }
    }

    private void Release()
    {
        if (--_watchers == 0)
        {
            Dispose();
        }
    }

    private sealed class Watcher(StatsMonitor owner) : IDisposable
    {
        private bool _disposed;

        public void Dispose()
        {
            if (!_disposed)
            {
                _disposed = true;
                owner.Release();
            }
        }
    }
}
