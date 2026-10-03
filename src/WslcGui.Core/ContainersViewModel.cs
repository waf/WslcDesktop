using System.Globalization;

using WslcGui.Engine;

namespace WslcGui.Core;

/// <summary>An immutable grid row. Equality is by value, so unchanged rows aren't re-rendered on refresh.</summary>
public sealed record ContainerRow(
    string Id,
    string Name,
    string Image,
    string Command,
    ContainerState State,
    string Status,
    string PortsText,
    DateTimeOffset? CreatedAt,
    string CreatedText,
    /// <summary>An action in flight ("Stopping…"), shown instead of the status.</summary>
    string? Pending,
    double? CpuPercent = null,
    long? MemoryBytes = null)
{
    public string CpuText => CpuPercent is { } cpu ? string.Create(CultureInfo.InvariantCulture, $"{cpu:0.0}%") : string.Empty;
    public string MemoryText => MemoryBytes is { } memory ? Formatting.Bytes(memory) : string.Empty;

    public string ShortId => Formatting.ShortId(Id);
    public bool IsRunning => State is ContainerState.Running or ContainerState.Restarting or ContainerState.Paused;
    public string StatusText => Pending ?? Status;

    public static ContainerRow From(ContainerSummary c, DateTimeOffset now) => new(
        c.Id,
        c.Name,
        c.Image,
        c.Command,
        c.State,
        c.Status,
        FormatPorts(c.Ports),
        c.CreatedAt,
        Formatting.Ago(c.CreatedAt, now),
        Pending: null);

    /// <summary>"15432:5432" style, matching what users type in run -p.</summary>
    public static string FormatPorts(IReadOnlyList<PortMapping> ports) => string.Join(", ", ports.Select(p =>
    {
        var protocol = p.Protocol == PortProtocol.Udp ? "/udp" : string.Empty;
        return p.HostPort is { } host
            ? string.Create(CultureInfo.InvariantCulture, $"{host}:{p.ContainerPort}{protocol}")
            : string.Create(CultureInfo.InvariantCulture, $"{p.ContainerPort}{protocol}");
    }));
}

public sealed class ContainersViewModel(
    IContainerQueries queries,
    IContainerLifecycle lifecycle,
    IEngineInfo engineInfo,
    IUserInteraction ui,
    IEventSource? events = null,
    TimeProvider? timeProvider = null,
    Func<IReadOnlyDictionary<string, StatsSample>>? latestStats = null)
    : ResourceListViewModel<ContainerRow>(engineInfo, ui, timeProvider), IDisposable
{
    /// <summary>How long to wait after an event before refreshing, so a burst (create, connect, start) is one refresh.</summary>
    public static readonly TimeSpan EventDebounce = TimeSpan.FromMilliseconds(250);

    /// <summary>Background poll interval while events are being watched; events cover state changes in between.</summary>
    public static readonly TimeSpan WatchedPollInterval = TimeSpan.FromSeconds(15);

    private bool _showAll = true;
    private CancellationTokenSource? _watch;
    private bool _eventRefreshQueued;

    /// <summary>
    /// Whether engine events are being watched. Only while containers are running: an open event stream keeps the
    /// engine VM alive, and with nothing running there is nothing to watch.
    /// </summary>
    public bool IsWatchingEvents => _watch is not null;

    /// <summary>Include stopped containers.</summary>
    public bool ShowAll
    {
        get => _showAll;
        set
        {
            if (SetProperty(ref _showAll, value))
            {
                _ = RefreshAsync();
            }
        }
    }

    /// <summary>Whether any container is running (or starting); stats and events are only worth reading then.</summary>
    public bool HasRunningContainers => AllRows.Any(row => row.IsRunning);

    /// <summary>Puts the latest resource usage on the rows (call when the stats monitor updates).</summary>
    public void ApplyStats(IReadOnlyDictionary<string, StatsSample> latest) =>
        UpdateRows(row => WithStats(row, latest), AllRows.Select(row => row.Id).ToHashSet());

    /// <summary>
    /// Whether the container is known to be gone. Only answerable when stopped containers are listed; otherwise a
    /// stopped container is simply not in the list.
    /// </summary>
    public bool IsKnownRemoved(string containerId) => _showAll && AllRows.All(row => row.Id != containerId);

    public void ReportError(string title, Exception exception) => Ui.ShowError(title, exception);

    public Task StartAsync(IReadOnlyList<ContainerRow> rows) =>
        RunAsync(rows.Where(r => !r.IsRunning).ToList(), "Starting…", "start", r => lifecycle.StartAsync(r.Id));

    public Task StopAsync(IReadOnlyList<ContainerRow> rows) =>
        RunAsync(rows.Where(r => r.IsRunning).ToList(), "Stopping…", "stop", r => lifecycle.StopAsync(r.Id));

    public Task RestartAsync(IReadOnlyList<ContainerRow> rows) =>
        RunAsync(rows, "Restarting…", "restart", r => lifecycle.RestartAsync(r.Id));

    public Task KillAsync(IReadOnlyList<ContainerRow> rows) =>
        RunAsync(rows.Where(r => r.IsRunning).ToList(), "Killing…", "kill", r => lifecycle.KillAsync(r.Id));

    public async Task RemoveAsync(IReadOnlyList<ContainerRow> rows)
    {
        if (rows.Count == 0)
        {
            return;
        }

        var running = rows.Count(r => r.IsRunning);
        var what = rows.Count == 1 ? $"'{rows[0].Name}'" : $"{rows.Count} containers";
        var message = running == 0
            ? $"Remove {what}? This can't be undone."
            : $"Remove {what}? {(running == 1 && rows.Count == 1 ? "It is" : $"{running} of them are")} running and will be stopped first. This can't be undone.";

        if (!await Ui.ConfirmAsync("Remove containers", message, "Remove"))
        {
            return;
        }

        await RunAsync(rows, "Removing…", "remove", r => lifecycle.RemoveAsync(r.Id, force: r.IsRunning));
    }

    public async Task PruneAsync()
    {
        if (!await Ui.ConfirmAsync("Remove stopped containers", "Remove all stopped containers? This can't be undone.", "Remove"))
        {
            return;
        }

        try
        {
            await lifecycle.PruneAsync();
            Ui.ShowToast("Removed stopped containers.");
        }
        catch (EngineException ex)
        {
            Ui.ShowError("Couldn't remove stopped containers", ex);
        }

        await RefreshAsync();
    }

    protected override async Task<IReadOnlyList<ContainerRow>> LoadAsync(CancellationToken cancellationToken)
    {
        var containers = await queries.ListAsync(_showAll, cancellationToken);
        var now = Time.GetUtcNow();
        var latest = latestStats?.Invoke();
        return containers
            .OrderByDescending(c => c.CreatedAt)
            .Select(c => WithStats(ContainerRow.From(c, now), latest))
            .ToList();
    }

    private static ContainerRow WithStats(ContainerRow row, IReadOnlyDictionary<string, StatsSample>? latest) =>
        row.IsRunning && latest is not null && latest.TryGetValue(row.Id, out var sample)
            ? row with { CpuPercent = sample.CpuPercent, MemoryBytes = sample.MemoryBytes }
            : row with { CpuPercent = null, MemoryBytes = null };

    protected override bool HasActiveWork => AllRows.Any(row => row.IsRunning || row.Pending is not null);

    protected override TimeSpan ActivePollInterval => IsWatchingEvents ? WatchedPollInterval : TimeSpan.Zero;

    protected override void OnLoaded()
    {
        if (AllRows.Any(row => row.IsRunning))
        {
            StartWatching();
        }
        else
        {
            StopWatching();
        }
    }

    public void Dispose() => StopWatching();

    public void StopWatching()
    {
        if (_watch is { } watch)
        {
            _watch = null;
            watch.Cancel();
            watch.Dispose();
            OnPropertyChanged(nameof(IsWatchingEvents));
        }
    }

    private void StartWatching()
    {
        if (events is null || _watch is not null)
        {
            return;
        }

        _watch = new CancellationTokenSource();
        OnPropertyChanged(nameof(IsWatchingEvents));
        _ = WatchAsync(events, _watch);
    }

    private async Task WatchAsync(IEventSource source, CancellationTokenSource watch)
    {
        try
        {
            await foreach (var engineEvent in source.WatchAsync(watch.Token))
            {
                if (engineEvent.Type == "container")
                {
                    _ = RefreshSoonAsync();
                }
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (EngineException)
        {
            // The stream broke; polling continues at the normal rate and the next load restarts watching.
        }
        finally
        {
            if (ReferenceEquals(_watch, watch))
            {
                _watch = null;
                watch.Dispose();
                OnPropertyChanged(nameof(IsWatchingEvents));
            }
        }
    }

    private async Task RefreshSoonAsync()
    {
        if (_eventRefreshQueued)
        {
            return;
        }

        _eventRefreshQueued = true;
        try
        {
            await Task.Delay(EventDebounce, Time);
        }
        finally
        {
            _eventRefreshQueued = false;
        }

        await RefreshAsync(RefreshReason.Event);
    }

    protected override string KeyOf(ContainerRow row) => row.Id;

    protected override string DisplayName(ContainerRow row) => $"'{row.Name}'";

    protected override bool Matches(ContainerRow row, string searchText) =>
        row.Name.Contains(searchText, StringComparison.OrdinalIgnoreCase)
        || row.Image.Contains(searchText, StringComparison.OrdinalIgnoreCase)
        || row.Id.StartsWith(searchText, StringComparison.OrdinalIgnoreCase);

    private Task RunAsync(IReadOnlyList<ContainerRow> rows, string pending, string verb, Func<ContainerRow, Task> action)
    {
        if (rows.Count == 0)
        {
            return Task.CompletedTask;
        }

        UpdateRows(row => row with { Pending = pending }, rows.Select(r => r.Id).ToHashSet());
        return RunForEachAsync(rows, verb, action);
    }
}
