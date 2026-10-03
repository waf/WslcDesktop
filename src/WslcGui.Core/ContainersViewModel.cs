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
    string? Pending)
{
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
    TimeProvider? timeProvider = null)
    : ResourceListViewModel<ContainerRow>(engineInfo, ui, timeProvider)
{
    private bool _showAll = true;

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
        return containers
            .OrderByDescending(c => c.CreatedAt)
            .Select(c => ContainerRow.From(c, now))
            .ToList();
    }

    protected override bool HasActiveWork => AllRows.Any(row => row.IsRunning || row.Pending is not null);

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
