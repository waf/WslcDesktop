using WslcGui.Engine;

namespace WslcGui.Core.Tests;

public class VolumesViewModelTests
{
    private readonly FakeEngine _engine = new();
    private readonly FakeUi _ui = new();
    private readonly FakeVolumes _volumes = new();

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Shows_which_containers_use_each_volume()
    {
        _volumes.Items.AddRange([new VolumeSummary("pgdata", "guest", "/var/lib/x", false), new VolumeSummary(new string('a', 64), "guest", null, true)]);
        _engine.Containers.Add(FakeEngine.Container("1", "db", ContainerState.Running) with { Mounts = ["pgdata", "/mnt/{guid}"] });
        var vm = new VolumesViewModel(_volumes, _engine, _engine, _ui);

        await vm.RefreshAsync(cancellationToken: Ct);

        Assert.Equal(["pgdata", "aaaaaaaaaaaa (anonymous)"], vm.Items.Select(v => v.DisplayName));
        Assert.Equal("db", vm.Items[0].UsageText);
        Assert.Equal("Unused", vm.Items[1].UsageText);
    }

    [Fact]
    public async Task Refuses_to_remove_volumes_in_use()
    {
        _volumes.Items.Add(new VolumeSummary("pgdata", "guest", null, false));
        _engine.Containers.Add(FakeEngine.Container("1", "db", ContainerState.Exited) with { Mounts = ["pgdata"] });
        var vm = new VolumesViewModel(_volumes, _engine, _engine, _ui);
        await vm.RefreshAsync(cancellationToken: Ct);

        await vm.RemoveAsync(vm.Items.ToList());

        Assert.Empty(_volumes.Calls);
        Assert.Contains("used by containers", Assert.Single(_ui.Errors), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Removes_unused_volume_after_confirming()
    {
        _volumes.Items.Add(new VolumeSummary("cache", "guest", null, false));
        var vm = new VolumesViewModel(_volumes, _engine, _engine, _ui);
        await vm.RefreshAsync(cancellationToken: Ct);

        await vm.RemoveAsync(vm.Items.ToList());

        Assert.Single(_ui.Confirmations);
        Assert.Equal(["remove cache"], _volumes.Calls);
    }

    private sealed class FakeVolumes : IVolumeService
    {
        public List<VolumeSummary> Items { get; } = [];
        public List<string> Calls { get; } = [];

        public Task<IReadOnlyList<VolumeSummary>> ListAsync(CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<VolumeSummary>>(Items.ToList());
        public Task<string> InspectAsync(string name, CancellationToken cancellationToken = default) => Task.FromResult("[]");
        public Task CreateAsync(string name, string? driver = null, CancellationToken cancellationToken = default) => Record($"create {name}");
        public Task RemoveAsync(string name, CancellationToken cancellationToken = default) => Record($"remove {name}");
        public Task PruneAsync(bool all, CancellationToken cancellationToken = default) => Record($"prune all={all}");

        private Task Record(string call)
        {
            Calls.Add(call);
            return Task.CompletedTask;
        }
    }
}

public class NetworksViewModelTests
{
    [Fact]
    public async Task Built_in_networks_are_listed_first_and_never_removed()
    {
        var engine = new FakeEngine();
        var ui = new FakeUi();
        var networks = new FakeNetworks();
        networks.Items.AddRange([new NetworkSummary("2", "backend", "bridge", null, false, false, false), new NetworkSummary("1", "bridge", "bridge", null, false, false, true)]);
        var vm = new NetworksViewModel(networks, engine, ui);
        await vm.RefreshAsync(cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(["bridge", "backend"], vm.Items.Select(n => n.Name));
        Assert.Equal("Built-in", vm.Items[0].KindText);

        await vm.RemoveAsync(vm.Items.ToList());

        Assert.Equal(["remove backend"], networks.Calls);
    }

    private sealed class FakeNetworks : INetworkService
    {
        public List<NetworkSummary> Items { get; } = [];
        public List<string> Calls { get; } = [];

        public Task<IReadOnlyList<NetworkSummary>> ListAsync(CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<NetworkSummary>>(Items.ToList());
        public Task<string> InspectAsync(string name, CancellationToken cancellationToken = default) => Task.FromResult("[]");
        public Task CreateAsync(string name, bool isInternal = false, CancellationToken cancellationToken = default) => Record($"create {name}");
        public Task RemoveAsync(string name, CancellationToken cancellationToken = default) => Record($"remove {name}");
        public Task PruneAsync(CancellationToken cancellationToken = default) => Record("prune");
        public Task ConnectAsync(string network, string containerId, CancellationToken cancellationToken = default) => Record($"connect {network} {containerId}");
        public Task DisconnectAsync(string network, string containerId, CancellationToken cancellationToken = default) => Record($"disconnect {network} {containerId}");

        private Task Record(string call)
        {
            Calls.Add(call);
            return Task.CompletedTask;
        }
    }
}

public class BuildAndCommandOutputTests
{
    private readonly FakeEngine _engine = new();

    [Fact]
    public async Task Build_streams_output_and_reports_success()
    {
        _engine.OutputLines.AddRange(["#1 load", "\x1b[32m#2 DONE\x1b[0m"]);
        using var vm = new BuildImageViewModel(_engine) { ContextDirectory = @"C:\app", Tag = "app:dev" };
        var lines = new List<string>();
        vm.Output.LineAdded += lines.Add;

        Assert.True(await vm.BuildAsync());

        await WaitUntil(() => lines.Count == 2);
        Assert.Equal(["#1 load", "#2 DONE"], lines);
        Assert.Equal(["build C:\\app tag=app:dev"], _engine.Calls);
        Assert.Equal("Built app:dev.", vm.Output.Status);
    }

    [Theory]
    [InlineData("", "app", "Choose the folder")]
    [InlineData(@"C:\app", "My App", "lowercase")]
    public async Task Build_validates_form(string context, string tag, string expectedError)
    {
        using var vm = new BuildImageViewModel(_engine) { ContextDirectory = context, Tag = tag };

        Assert.False(await vm.BuildAsync());

        Assert.Contains(expectedError, vm.Error, StringComparison.Ordinal);
        Assert.Empty(_engine.Calls);
    }

    [Fact]
    public async Task Failed_operation_shows_the_error_as_status()
    {
        using var output = new CommandOutputViewModel();

        var ok = await output.RunAsync("Pushing…", "Pushed.", (_, _) => throw new EngineException(EngineErrorKind.Unknown, "denied"));

        Assert.False(ok);
        Assert.Equal("denied", output.Status);
        Assert.False(output.Succeeded);
        Assert.False(output.IsBusy);
    }

    [Fact]
    public async Task Cancel_stops_the_operation()
    {
        using var output = new CommandOutputViewModel();
        var started = new TaskCompletionSource();

        var run = output.RunAsync("Pushing…", "Pushed.", async (_, ct) =>
        {
            started.SetResult();
            await Task.Delay(Timeout.Infinite, ct);
        });
        await started.Task;
        output.Cancel();

        Assert.False(await run);
        Assert.Equal("Cancelled.", output.Status);
    }

    private static async Task WaitUntil(Func<bool> condition)
    {
        for (var i = 0; i < 200 && !condition(); i++)
        {
            await Task.Delay(5, TestContext.Current.CancellationToken);
        }
    }
}

public class RegistryAndTroubleshootTests
{
    [Fact]
    public async Task Login_clears_the_password_and_shows_errors()
    {
        var registry = new FakeRegistry { Error = new EngineException(EngineErrorKind.Unknown, "unauthorized") };
        var vm = new RegistryLoginViewModel(registry) { Username = "me", Password = "secret" };

        Assert.False(await vm.LoginAsync());

        Assert.Equal("unauthorized", vm.Error);
        Assert.Equal(string.Empty, vm.Password);
        Assert.Equal((null, "me", "secret"), registry.Last);
    }

    [Fact]
    public void Command_log_is_newest_first_and_bounded()
    {
        var vm = new TroubleshootViewModel(new FakeMaintenance(), new FakeUi());

        for (var i = 0; i < TroubleshootViewModel.MaxLogEntries + 5; i++)
        {
            vm.Record(new CommandLogEntry(DateTimeOffset.UnixEpoch, $"wslc cmd {i}", 0, TimeSpan.FromMilliseconds(40), null));
        }

        Assert.Equal(TroubleshootViewModel.MaxLogEntries, vm.RecentCommands.Count);
        Assert.Equal($"wslc cmd {TroubleshootViewModel.MaxLogEntries + 4}", vm.RecentCommands[0].Command);
    }

    [Fact]
    public async Task Diagnostics_include_details_and_commands()
    {
        var vm = new TroubleshootViewModel(new FakeMaintenance(), new FakeUi());
        await vm.RefreshAsync();
        vm.Record(new CommandLogEntry(DateTimeOffset.UnixEpoch, "wslc version", 1, TimeSpan.FromSeconds(2), "boom"));

        var text = vm.GetDiagnosticsText();

        Assert.Contains("Client.Version: 3.0.1.0", text, StringComparison.Ordinal);
        Assert.Contains("exit 1: boom  wslc version", text, StringComparison.Ordinal);
    }

    private sealed class FakeRegistry : IRegistryService
    {
        public EngineException? Error { get; init; }
        public (string? Server, string User, string Password)? Last { get; private set; }

        public Task LoginAsync(string? server, string username, string password, CancellationToken cancellationToken = default)
        {
            Last = (server, username, password);
            return Error is { } error ? Task.FromException(error) : Task.CompletedTask;
        }

        public Task LogoutAsync(string? server, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    private sealed class FakeMaintenance : IEngineMaintenance
    {
        public Task<IReadOnlyList<KeyValuePair<string, string>>> GetDetailsAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<KeyValuePair<string, string>>>([new("Client.Version", "3.0.1.0")]);

        public void OpenSettingsFile()
        {
        }

        public Task RestartEngineAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
    }
}
