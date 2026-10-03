using WslcGui.Engine;

namespace WslcGui.Core.Tests;

public class EngineStatusViewModelTests
{
    [Fact]
    public async Task Refresh_shows_ready_with_version()
    {
        var vm = new EngineStatusViewModel(new StubEngineInfo(new EngineHealth(EngineHealthStatus.Ready, new EngineVersion("3.0.1.0"), null)));
        var changed = new List<string?>();
        vm.PropertyChanged += (_, e) => changed.Add(e.PropertyName);

        await vm.RefreshAsync(TestContext.Current.CancellationToken);

        Assert.Equal(EngineHealthStatus.Ready, vm.Status);
        Assert.Equal("3.0.1.0", vm.Version);
        Assert.Contains("3.0.1.0", vm.StatusText, StringComparison.Ordinal);
        Assert.Contains(nameof(vm.StatusText), changed);
    }

    [Fact]
    public async Task Refresh_explains_how_to_install_when_missing()
    {
        var vm = new EngineStatusViewModel(new StubEngineInfo(new EngineHealth(EngineHealthStatus.NotInstalled, null, "wslc.exe was not found.")));

        await vm.RefreshAsync(TestContext.Current.CancellationToken);

        Assert.Equal(EngineHealthStatus.NotInstalled, vm.Status);
        Assert.Contains("wsl --update", vm.StatusText, StringComparison.Ordinal);
    }

    private sealed class StubEngineInfo(EngineHealth health) : IEngineInfo
    {
        public Task<EngineVersion> GetVersionAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(health.Version ?? throw new EngineException(EngineErrorKind.Unavailable, "missing"));

        public Task<EngineHealth> CheckHealthAsync(CancellationToken cancellationToken = default) => Task.FromResult(health);

        public Task<EngineRuntimeState> GetRuntimeStateAsync(CancellationToken cancellationToken = default) => Task.FromResult(EngineRuntimeState.Running);
    }
}
