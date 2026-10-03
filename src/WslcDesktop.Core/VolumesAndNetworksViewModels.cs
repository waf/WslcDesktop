using WslcDesktop.Engine;

namespace WslcDesktop.Core;

/// <param name="UsedBy">Names of containers (in any state) that mount the volume.</param>
public sealed record VolumeRow(string Name, string Driver, string Mountpoint, bool IsAnonymous, string UsedBy, string? Pending)
{
    public bool IsInUse => UsedBy.Length > 0;
    public string DisplayName => IsAnonymous ? $"{Formatting.ShortId(Name)} (anonymous)" : Name;
    public string UsageText => Pending ?? (IsInUse ? UsedBy : "Unused");
}

public sealed class VolumesViewModel(IVolumeService volumes, IContainerQueries containers, IEngineInfo engineInfo, IUserInteraction ui)
    : ResourceListViewModel<VolumeRow>(engineInfo, ui, timeProvider: null)
{
    public async Task CreateAsync(string name)
    {
        try
        {
            await volumes.CreateAsync(name.Trim());
            Ui.ShowToast($"Created volume {name.Trim()}.");
        }
        catch (EngineException ex)
        {
            Ui.ShowError("Couldn't create the volume", ex);
        }

        await RefreshAsync();
    }

    public async Task RemoveAsync(IReadOnlyList<VolumeRow> rows)
    {
        if (rows.Count == 0)
        {
            return;
        }

        var inUse = rows.Where(r => r.IsInUse).ToList();
        if (inUse.Count > 0)
        {
            Ui.ShowError(
                "Volumes in use",
                new EngineException(EngineErrorKind.Conflict, $"{string.Join(", ", inUse.Select(r => r.DisplayName))} {(inUse.Count == 1 ? "is" : "are")} used by containers. Remove those containers first."));
            return;
        }

        var what = rows.Count == 1 ? $"volume '{rows[0].DisplayName}'" : $"{rows.Count} volumes";
        if (!await Ui.ConfirmAsync("Remove volumes", $"Remove {what}? The data in {(rows.Count == 1 ? "it" : "them")} is deleted.", "Remove"))
        {
            return;
        }

        UpdateRows(row => row with { Pending = "Removing…" }, rows.Select(r => r.Name).ToHashSet());
        await RunForEachAsync(rows, "remove", row => volumes.RemoveAsync(row.Name));
    }

    /// <param name="all">Also remove unused named volumes, not just anonymous ones.</param>
    public async Task PruneAsync(bool all)
    {
        var message = all ? "Remove all volumes not used by any container? Their data is deleted." : "Remove anonymous volumes not used by any container?";
        if (!await Ui.ConfirmAsync("Remove unused volumes", message, "Remove"))
        {
            return;
        }

        try
        {
            await volumes.PruneAsync(all);
            Ui.ShowToast("Removed unused volumes.");
        }
        catch (EngineException ex)
        {
            Ui.ShowError("Couldn't remove unused volumes", ex);
        }

        await RefreshAsync();
    }

    public Task<string> InspectAsync(VolumeRow row) => volumes.InspectAsync(row.Name);

    protected override async Task<IReadOnlyList<VolumeRow>> LoadAsync(CancellationToken cancellationToken)
    {
        var list = await volumes.ListAsync(cancellationToken);
        var all = await containers.ListAsync(includeStopped: true, cancellationToken);
        return list
            .OrderBy(v => v.IsAnonymous)
            .ThenBy(v => v.Name, StringComparer.OrdinalIgnoreCase)
            .Select(v => new VolumeRow(
                v.Name,
                v.Driver,
                v.Mountpoint ?? string.Empty,
                v.IsAnonymous,
                string.Join(", ", all.Where(c => c.Mounts?.Contains(v.Name) == true).Select(c => c.Name)),
                Pending: null))
            .ToList();
    }

    protected override string KeyOf(VolumeRow row) => row.Name;

    protected override string DisplayName(VolumeRow row) => $"volume '{row.DisplayName}'";

    protected override bool Matches(VolumeRow row, string searchText) =>
        row.Name.Contains(searchText, StringComparison.OrdinalIgnoreCase) || row.UsedBy.Contains(searchText, StringComparison.OrdinalIgnoreCase);
}

public sealed record NetworkRow(string Id, string Name, string Driver, DateTimeOffset? CreatedAt, string CreatedText, bool IsInternal, bool IsBuiltIn, string? Pending)
{
    public string ShortId => Formatting.ShortId(Id);
    public string KindText => Pending ?? (IsBuiltIn ? "Built-in" : IsInternal ? "Internal" : "User-defined");
}

public sealed class NetworksViewModel(INetworkService networks, IEngineInfo engineInfo, IUserInteraction ui, TimeProvider? timeProvider = null)
    : ResourceListViewModel<NetworkRow>(engineInfo, ui, timeProvider)
{
    public async Task CreateAsync(string name, bool isInternal)
    {
        try
        {
            await networks.CreateAsync(name.Trim(), isInternal);
            Ui.ShowToast($"Created network {name.Trim()}.");
        }
        catch (EngineException ex)
        {
            Ui.ShowError("Couldn't create the network", ex);
        }

        await RefreshAsync();
    }

    public async Task RemoveAsync(IReadOnlyList<NetworkRow> rows)
    {
        var removable = rows.Where(r => !r.IsBuiltIn).ToList();
        if (removable.Count == 0)
        {
            return;
        }

        var what = removable.Count == 1 ? $"network '{removable[0].Name}'" : $"{removable.Count} networks";
        if (!await Ui.ConfirmAsync("Remove networks", $"Remove {what}?", "Remove"))
        {
            return;
        }

        UpdateRows(row => row with { Pending = "Removing…" }, removable.Select(r => r.Name).ToHashSet());
        await RunForEachAsync(removable, "remove", row => networks.RemoveAsync(row.Name));
    }

    public async Task PruneAsync()
    {
        if (!await Ui.ConfirmAsync("Remove unused networks", "Remove all user-defined networks not used by any container?", "Remove"))
        {
            return;
        }

        try
        {
            await networks.PruneAsync();
            Ui.ShowToast("Removed unused networks.");
        }
        catch (EngineException ex)
        {
            Ui.ShowError("Couldn't remove unused networks", ex);
        }

        await RefreshAsync();
    }

    public Task<string> InspectAsync(NetworkRow row) => networks.InspectAsync(row.Name);

    protected override async Task<IReadOnlyList<NetworkRow>> LoadAsync(CancellationToken cancellationToken)
    {
        var list = await networks.ListAsync(cancellationToken);
        var now = Time.GetUtcNow();
        return list
            .OrderBy(n => !n.IsBuiltIn)
            .ThenBy(n => n.Name, StringComparer.OrdinalIgnoreCase)
            .Select(n => new NetworkRow(n.Id, n.Name, n.Driver, n.CreatedAt, Formatting.Ago(n.CreatedAt, now), n.IsInternal, n.IsBuiltIn, Pending: null))
            .ToList();
    }

    // The default bridge network gets a new ID on every VM boot (S1), so key by name.
    protected override string KeyOf(NetworkRow row) => row.Name;

    protected override string DisplayName(NetworkRow row) => $"network '{row.Name}'";

    protected override bool Matches(NetworkRow row, string searchText) =>
        row.Name.Contains(searchText, StringComparison.OrdinalIgnoreCase) || row.Id.StartsWith(searchText, StringComparison.OrdinalIgnoreCase);
}
