using Microsoft.Extensions.Time.Testing;

using WslcDesktop.Engine;

namespace WslcDesktop.Core.Tests;

public class ContainersViewModelTests
{
    private readonly FakeEngine _engine = new();
    private readonly FakeUi _ui = new();

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private readonly FakeTimeProvider _time = new(new DateTimeOffset(2026, 10, 3, 12, 0, 0, TimeSpan.Zero));

    private ContainersViewModel CreateViewModel() => new(_engine, _engine, _engine, _ui, timeProvider: _time);

    [Fact]
    public async Task Refresh_lists_newest_first()
    {
        _engine.Containers.Add(FakeEngine.Container("a", "old", ContainerState.Exited, minutesAgo: 60));
        _engine.Containers.Add(FakeEngine.Container("b", "new", ContainerState.Running, minutesAgo: 1));
        var vm = CreateViewModel();

        await vm.RefreshAsync(cancellationToken: Ct);

        Assert.Equal(["new", "old"], vm.Items.Select(r => r.Name));
        Assert.Null(vm.ErrorText);
    }

    [Fact]
    public async Task Background_refresh_does_not_wake_an_idle_engine()
    {
        _engine.Containers.Add(FakeEngine.Container("a", "web", ContainerState.Exited));
        var vm = CreateViewModel();
        await vm.RefreshAsync(cancellationToken: Ct);
        _engine.RuntimeState = EngineRuntimeState.Idle;

        await vm.RefreshAsync(RefreshReason.Background, Ct);

        Assert.Equal(1, _engine.ListCalls);
        Assert.True(vm.IsEngineIdle);
        Assert.Single(vm.Items);
    }

    [Fact]
    public async Task Background_refresh_never_starts_an_idle_engine_even_before_the_first_load()
    {
        _engine.RuntimeState = EngineRuntimeState.Idle;
        var vm = CreateViewModel();

        await vm.RefreshAsync(RefreshReason.Background, Ct);

        Assert.Equal(0, _engine.ListCalls);
        Assert.True(vm.IsEngineIdle);
    }

    [Fact]
    public async Task Starting_engine_is_only_reported_for_a_real_cold_start()
    {
        var vm = CreateViewModel();
        var starting = new List<bool>();
        vm.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(vm.IsStartingEngine))
            {
                starting.Add(vm.IsStartingEngine);
            }
        };

        await vm.RefreshAsync(cancellationToken: Ct);
        Assert.Empty(starting);

        _engine.RuntimeState = EngineRuntimeState.Idle;
        await vm.RefreshAsync(cancellationToken: Ct);
        Assert.Equal([true, false], starting);
    }

    [Fact]
    public async Task Background_refresh_polls_while_containers_run()
    {
        _engine.Containers.Add(FakeEngine.Container("a", "web", ContainerState.Running));
        var vm = CreateViewModel();
        await vm.RefreshAsync(cancellationToken: Ct);

        await vm.RefreshAsync(RefreshReason.Background, Ct);

        Assert.Equal(2, _engine.ListCalls);
        Assert.False(vm.IsEngineIdle);
    }

    [Fact]
    public async Task Background_refresh_slows_down_when_nothing_runs_so_the_engine_can_idle()
    {
        _engine.Containers.Add(FakeEngine.Container("a", "web", ContainerState.Exited));
        var vm = CreateViewModel();
        await vm.RefreshAsync(cancellationToken: Ct);

        _time.Advance(TimeSpan.FromSeconds(4));
        await vm.RefreshAsync(RefreshReason.Background, Ct);
        Assert.Equal(1, _engine.ListCalls);

        _time.Advance(ContainersViewModel.QuietPollInterval);
        await vm.RefreshAsync(RefreshReason.Background, Ct);
        Assert.Equal(2, _engine.ListCalls);
    }

    [Fact]
    public async Task User_refresh_always_queries()
    {
        var vm = CreateViewModel();
        await vm.RefreshAsync(cancellationToken: Ct);

        await vm.RefreshAsync(cancellationToken: Ct);

        Assert.Equal(2, _engine.ListCalls);
    }

    [Fact]
    public async Task Unchanged_rows_keep_their_instances_across_refreshes()
    {
        _engine.Containers.Add(FakeEngine.Container("a", "web", ContainerState.Running));
        _engine.Containers.Add(FakeEngine.Container("b", "db", ContainerState.Running));
        var vm = CreateViewModel();
        await vm.RefreshAsync(cancellationToken: Ct);
        var web = vm.Items.Single(r => r.Name == "web");

        _engine.Containers[1] = _engine.Containers[1] with { State = ContainerState.Exited, Status = "Exited (0)" };
        await vm.RefreshAsync(cancellationToken: Ct);

        Assert.Same(web, vm.Items.Single(r => r.Name == "web"));
        Assert.Equal(ContainerState.Exited, vm.Items.Single(r => r.Name == "db").State);
    }

    [Fact]
    public async Task Search_filters_by_name_image_or_id_prefix()
    {
        _engine.Containers.Add(FakeEngine.Container("abc123", "web", ContainerState.Running));
        _engine.Containers.Add(FakeEngine.Container("def456", "db", ContainerState.Running));
        var vm = CreateViewModel();
        await vm.RefreshAsync(cancellationToken: Ct);

        vm.SearchText = "def";
        Assert.Equal(["db"], vm.Items.Select(r => r.Name));

        vm.SearchText = "WE";
        Assert.Equal(["web"], vm.Items.Select(r => r.Name));

        vm.SearchText = "";
        Assert.Equal(2, vm.Items.Count);
    }

    [Fact]
    public async Task Stop_only_targets_running_containers_and_refreshes()
    {
        _engine.Containers.Add(FakeEngine.Container("a", "web", ContainerState.Running));
        _engine.Containers.Add(FakeEngine.Container("b", "old", ContainerState.Exited));
        var vm = CreateViewModel();
        await vm.RefreshAsync(cancellationToken: Ct);

        await vm.StopAsync(vm.Items.ToList());

        Assert.Equal(["stop a"], _engine.Calls);
        Assert.Equal(2, _engine.ListCalls);
    }

    [Fact]
    public async Task Remove_asks_first_and_forces_running_containers()
    {
        _engine.Containers.Add(FakeEngine.Container("a", "web", ContainerState.Running));
        _engine.Containers.Add(FakeEngine.Container("b", "old", ContainerState.Exited));
        var vm = CreateViewModel();
        await vm.RefreshAsync(cancellationToken: Ct);

        await vm.RemoveAsync(vm.Items.ToList());

        Assert.Contains("1 of them are running", Assert.Single(_ui.Confirmations), StringComparison.Ordinal);
        Assert.Equal(["remove a force=True", "remove b force=False"], _engine.Calls.Order());
    }

    [Fact]
    public async Task Declined_remove_does_nothing()
    {
        _engine.Containers.Add(FakeEngine.Container("a", "web", ContainerState.Exited));
        var vm = CreateViewModel();
        await vm.RefreshAsync(cancellationToken: Ct);
        _ui.ConfirmResult = false;

        await vm.RemoveAsync(vm.Items.ToList());

        Assert.Empty(_engine.Calls);
    }

    [Fact]
    public async Task Failed_action_is_reported_and_list_refreshed()
    {
        _engine.Containers.Add(FakeEngine.Container("a", "web", ContainerState.Exited));
        _engine.Failures["start a"] = new EngineException(EngineErrorKind.Unknown, "boom");
        var vm = CreateViewModel();
        await vm.RefreshAsync(cancellationToken: Ct);

        await vm.StartAsync(vm.Items.ToList());

        Assert.Equal(["Couldn't start 'web': boom"], _ui.Errors);
        Assert.Null(vm.Items.Single().Pending);
    }

    [Fact]
    public async Task Refresh_failure_is_shown_as_error_text()
    {
        var vm = new ContainersViewModel(new ThrowingQueries(), _engine, _engine, _ui);

        await vm.RefreshAsync(cancellationToken: Ct);

        Assert.Equal("engine down", vm.ErrorText);
        Assert.False(vm.IsLoading);
    }

    private sealed class ThrowingQueries : IContainerQueries
    {
        public Task<IReadOnlyList<ContainerSummary>> ListAsync(bool includeStopped, CancellationToken cancellationToken = default) =>
            throw new EngineException(EngineErrorKind.Unavailable, "engine down");

        public Task<string> InspectAsync(string containerId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }
}
