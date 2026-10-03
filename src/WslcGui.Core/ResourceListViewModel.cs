using System.Collections.ObjectModel;

using WslcGui.Engine;

namespace WslcGui.Core;

public enum RefreshReason
{
    /// <summary>The user asked (page opened, refresh button, after an action). Always queries the engine.</summary>
    User,

    /// <summary>A periodic poll. Skipped while the engine VM is idle so polling never wakes it.</summary>
    Background,
}

/// <summary>
/// Shared behavior of the list pages (containers, images, ...): loading, background refresh that respects the
/// engine's idle VM, search filtering, and keeping <see cref="Items"/> stable across refreshes.
/// </summary>
public abstract class ResourceListViewModel<TRow> : ObservableObject
    where TRow : class
{
    /// <summary>
    /// Background refresh interval when nothing is running. Any engine command resets the VM's idle timer
    /// (30 s by default), so polling faster than this would keep an otherwise idle VM alive forever.
    /// </summary>
    public static readonly TimeSpan QuietPollInterval = TimeSpan.FromSeconds(45);

    private readonly IEngineInfo _engineInfo;
    private readonly TimeProvider _time;
    private DateTimeOffset _lastLoad = DateTimeOffset.MinValue;
    private IReadOnlyList<TRow> _all = [];
    private string _searchText = string.Empty;
    private bool _isLoading;
    private bool _hasLoaded;
    private bool _isEngineIdle;
    private string? _errorText;
    private bool _refreshing;

    protected ResourceListViewModel(IEngineInfo engineInfo, IUserInteraction ui, TimeProvider? timeProvider)
    {
        _engineInfo = engineInfo;
        _time = timeProvider ?? TimeProvider.System;
        Ui = ui;
    }

    /// <summary>The rows shown, after filtering. Mutated only on the UI thread.</summary>
    public ObservableCollection<TRow> Items { get; } = [];

    public string SearchText
    {
        get => _searchText;
        set
        {
            if (SetProperty(ref _searchText, value ?? string.Empty))
            {
                ApplyFilter();
            }
        }
    }

    /// <summary>True while the first load (which may cold-start the engine VM) is in progress.</summary>
    public bool IsLoading
    {
        get => _isLoading;
        private set => SetProperty(ref _isLoading, value);
    }

    /// <summary>The engine VM is idle; the list shows the last known state and background refresh is paused.</summary>
    public bool IsEngineIdle
    {
        get => _isEngineIdle;
        private set => SetProperty(ref _isEngineIdle, value);
    }

    /// <summary>The last refresh failure, or null.</summary>
    public string? ErrorText
    {
        get => _errorText;
        private set => SetProperty(ref _errorText, value);
    }

    /// <summary>Total rows before filtering.</summary>
    public int TotalCount => _all.Count;

    /// <summary>All rows before filtering.</summary>
    protected IReadOnlyList<TRow> AllRows => _all;

    protected IUserInteraction Ui { get; }

    protected TimeProvider Time => _time;

    /// <summary>
    /// Whether something is going on that's worth polling for at the normal rate (for example running containers).
    /// Otherwise background refresh drops to <see cref="QuietPollInterval"/> so the engine can go idle.
    /// </summary>
    protected virtual bool HasActiveWork => false;

    public async Task RefreshAsync(RefreshReason reason = RefreshReason.User, CancellationToken cancellationToken = default)
    {
        if (_refreshing)
        {
            return;
        }

        _refreshing = true;
        try
        {
            if (reason == RefreshReason.Background)
            {
                var state = await _engineInfo.GetRuntimeStateAsync(cancellationToken);
                IsEngineIdle = state == EngineRuntimeState.Idle;
                if (IsEngineIdle && _hasLoaded)
                {
                    return;
                }

                if (_hasLoaded && !HasActiveWork && _time.GetUtcNow() - _lastLoad < QuietPollInterval)
                {
                    return;
                }
            }

            IsLoading = !_hasLoaded;
            _all = await LoadAsync(cancellationToken);
            _hasLoaded = true;
            _lastLoad = _time.GetUtcNow();
            IsEngineIdle = false;
            ErrorText = null;
            OnPropertyChanged(nameof(TotalCount));
            ApplyFilter();
        }
        catch (EngineException ex)
        {
            ErrorText = ex.Message;
        }
        finally
        {
            IsLoading = false;
            _refreshing = false;
        }
    }

    protected abstract Task<IReadOnlyList<TRow>> LoadAsync(CancellationToken cancellationToken);

    protected abstract string KeyOf(TRow row);

    /// <summary>Whether a row matches the (non-empty, trimmed) search text.</summary>
    protected abstract bool Matches(TRow row, string searchText);

    /// <summary>Replaces rows in place (for example to show a pending action) without reloading.</summary>
    protected void UpdateRows(Func<TRow, TRow> update, IReadOnlySet<string> keys)
    {
        _all = _all.Select(row => keys.Contains(KeyOf(row)) ? update(row) : row).ToList();
        ApplyFilter();
    }

    /// <summary>
    /// Runs an engine action for each target concurrently, reports failures, then refreshes.
    /// </summary>
    protected async Task RunForEachAsync(IReadOnlyList<TRow> targets, string verb, Func<TRow, Task> action)
    {
        var failures = new List<(TRow Row, Exception Error)>();
        await Task.WhenAll(targets.Select(async row =>
        {
            try
            {
                await action(row);
            }
            catch (EngineException ex)
            {
                lock (failures)
                {
                    failures.Add((row, ex));
                }
            }
        }));

        foreach (var (row, error) in failures)
        {
            Ui.ShowError($"Couldn't {verb} {DisplayName(row)}", error);
        }

        await RefreshAsync(RefreshReason.User);
    }

    protected abstract string DisplayName(TRow row);

    private void ApplyFilter()
    {
        var search = _searchText.Trim();
        var visible = search.Length == 0 ? _all : _all.Where(row => Matches(row, search)).ToList();
        CollectionSync.Sync(Items, visible, KeyOf);
    }
}
