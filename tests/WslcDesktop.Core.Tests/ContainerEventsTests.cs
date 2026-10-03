using System.Runtime.CompilerServices;
using System.Threading.Channels;

using Microsoft.Extensions.Time.Testing;

using WslcDesktop.Engine;

namespace WslcDesktop.Core.Tests;

public class ContainerEventsTests
{
    private readonly FakeEngine _engine = new();
    private readonly FakeUi _ui = new();
    private readonly FakeEvents _events = new();
    private readonly FakeTimeProvider _time = new(new DateTimeOffset(2026, 10, 3, 12, 0, 0, TimeSpan.Zero));

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private ContainersViewModel CreateViewModel() => new(_engine, _engine, _engine, _ui, _events, _time);

    [Fact]
    public async Task Watches_events_only_while_containers_run()
    {
        _engine.Containers.Add(FakeEngine.Container("a", "web", ContainerState.Exited));
        using var vm = CreateViewModel();

        await vm.RefreshAsync(cancellationToken: Ct);
        Assert.False(vm.IsWatchingEvents);

        _engine.Containers[0] = _engine.Containers[0] with { State = ContainerState.Running };
        await vm.RefreshAsync(cancellationToken: Ct);
        Assert.True(vm.IsWatchingEvents);
        Assert.Equal(1, _events.Subscriptions);

        _engine.Containers[0] = _engine.Containers[0] with { State = ContainerState.Exited };
        await vm.RefreshAsync(cancellationToken: Ct);
        Assert.False(vm.IsWatchingEvents);
        Assert.True(_events.LastCancelled);
    }

    [Fact]
    public async Task Container_event_triggers_a_debounced_refresh()
    {
        _engine.Containers.Add(FakeEngine.Container("a", "web", ContainerState.Running));
        using var vm = CreateViewModel();
        await vm.RefreshAsync(cancellationToken: Ct);
        var calls = _engine.ListCalls;

        _engine.Containers.Add(FakeEngine.Container("b", "db", ContainerState.Running));
        await _events.SendAsync(new EngineEvent("container", "create", "b", new Dictionary<string, string>(), _time.GetUtcNow()));
        await _events.SendAsync(new EngineEvent("container", "start", "b", new Dictionary<string, string>(), _time.GetUtcNow()));
        Assert.Equal(calls, _engine.ListCalls);

        // The watcher reads events asynchronously; keep the fake clock moving until the debounced refresh runs.
        await WaitUntil(() =>
        {
            _time.Advance(ContainersViewModel.EventDebounce);
            return vm.Items.Count == 2;
        });

        Assert.Equal(2, vm.Items.Count);
        Assert.InRange(_engine.ListCalls, calls + 1, calls + 2);
    }

    [Fact]
    public async Task Polls_less_often_while_watching()
    {
        _engine.Containers.Add(FakeEngine.Container("a", "web", ContainerState.Running));
        using var vm = CreateViewModel();
        await vm.RefreshAsync(cancellationToken: Ct);

        _time.Advance(TimeSpan.FromSeconds(4));
        await vm.RefreshAsync(RefreshReason.Background, Ct);
        Assert.Equal(1, _engine.ListCalls);

        _time.Advance(ContainersViewModel.WatchedPollInterval);
        await vm.RefreshAsync(RefreshReason.Background, Ct);
        Assert.Equal(2, _engine.ListCalls);
    }

    private static async Task WaitUntil(Func<bool> condition)
    {
        for (var i = 0; i < 200 && !condition(); i++)
        {
            await Task.Delay(10, Ct);
        }
    }

    private sealed class FakeEvents : IEventSource
    {
        private Channel<EngineEvent> _channel = Channel.CreateUnbounded<EngineEvent>();

        public int Subscriptions { get; private set; }
        public bool LastCancelled { get; private set; }

        public ValueTask SendAsync(EngineEvent e) => _channel.Writer.WriteAsync(e);

        public async IAsyncEnumerable<EngineEvent> WatchAsync([EnumeratorCancellation] CancellationToken cancellationToken)
        {
            Subscriptions++;
            _channel = Channel.CreateUnbounded<EngineEvent>();
            using var registration = cancellationToken.Register(() => LastCancelled = true);
            await foreach (var e in _channel.Reader.ReadAllAsync(cancellationToken))
            {
                yield return e;
            }
        }
    }
}
