using WslcGui.Engine;

namespace WslcGui.Core.Tests;

internal sealed class FakeEngine : IEngineInfo, IContainerQueries, IContainerLifecycle, IImageService
{
    public List<ContainerSummary> Containers { get; } = [];
    public List<ImageSummary> Images { get; } = [];
    public EngineRuntimeState RuntimeState { get; set; } = EngineRuntimeState.Running;
    public List<string> Calls { get; } = [];
    public int ListCalls { get; private set; }

    /// <summary>Calls whose description starts with this prefix fail with this error.</summary>
    public Dictionary<string, EngineException> Failures { get; } = [];

    public static ContainerSummary Container(string id, string name, ContainerState state, int minutesAgo = 1) => new(
        id, name, "alpine:latest", "sleep infinity", state, state == ContainerState.Running ? "Up" : "Exited (0)",
        DateTimeOffset.UnixEpoch.AddDays(20000).AddMinutes(-minutesAgo), []);

    public Task<EngineVersion> GetVersionAsync(CancellationToken cancellationToken = default) => Task.FromResult(new EngineVersion("3.0.1.0"));

    public Task<EngineHealth> CheckHealthAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult(new EngineHealth(EngineHealthStatus.Ready, new EngineVersion("3.0.1.0"), null));

    public Task<EngineRuntimeState> GetRuntimeStateAsync(CancellationToken cancellationToken = default) => Task.FromResult(RuntimeState);

    Task<IReadOnlyList<ContainerSummary>> IContainerQueries.ListAsync(bool includeStopped, CancellationToken cancellationToken)
    {
        ListCalls++;
        return Task.FromResult<IReadOnlyList<ContainerSummary>>(
            Containers.Where(c => includeStopped || c.State == ContainerState.Running).ToList());
    }

    Task<string> IContainerQueries.InspectAsync(string containerId, CancellationToken cancellationToken) => Task.FromResult("[]");

    public Task<string> RunAsync(RunSpec spec, CancellationToken cancellationToken = default) => Record($"run {spec.Image}", "id");
    public Task<string> CreateAsync(RunSpec spec, CancellationToken cancellationToken = default) => Record($"create {spec.Image}", "id");
    public Task StartAsync(string containerId, CancellationToken cancellationToken = default) => Record($"start {containerId}");
    public Task StopAsync(string containerId, TimeSpan? timeout = null, CancellationToken cancellationToken = default) => Record($"stop {containerId}");
    public Task RestartAsync(string containerId, CancellationToken cancellationToken = default) => Record($"restart {containerId}");
    public Task KillAsync(string containerId, CancellationToken cancellationToken = default) => Record($"kill {containerId}");
    public Task RemoveAsync(string containerId, bool force, CancellationToken cancellationToken = default) => Record($"remove {containerId} force={force}");
    Task IContainerLifecycle.PruneAsync(CancellationToken cancellationToken) => Record("container prune");

    Task<IReadOnlyList<ImageSummary>> IImageService.ListAsync(CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<ImageSummary>>(Images.ToList());

    Task<string> IImageService.InspectAsync(string image, CancellationToken cancellationToken) => Task.FromResult("[]");
    public Task PullAsync(string reference, IProgress<PullProgress>? progress = null, CancellationToken cancellationToken = default) => Record($"pull {reference}");
    Task IImageService.RemoveAsync(string image, bool force, CancellationToken cancellationToken) => Record($"rmi {image} force={force}");
    public Task TagAsync(string source, string target, CancellationToken cancellationToken = default) => Record($"tag {source} {target}");
    public Task PruneAsync(bool all, CancellationToken cancellationToken = default) => Record($"image prune all={all}");

    private Task Record(string call) => Record<object?>(call, null);

    private Task<T> Record<T>(string call, T result)
    {
        Calls.Add(call);
        foreach (var (prefix, error) in Failures)
        {
            if (call.StartsWith(prefix, StringComparison.Ordinal))
            {
                return Task.FromException<T>(error);
            }
        }

        return Task.FromResult(result);
    }
}

internal sealed class FakeUi : IUserInteraction
{
    public bool ConfirmResult { get; set; } = true;
    public List<string> Toasts { get; } = [];
    public List<string> Errors { get; } = [];
    public List<string> Confirmations { get; } = [];

    public void ShowToast(string message) => Toasts.Add(message);

    public void ShowError(string title, Exception exception) => Errors.Add($"{title}: {exception.Message}");

    public Task<bool> ConfirmAsync(string title, string message, string confirmText)
    {
        Confirmations.Add(message);
        return Task.FromResult(ConfirmResult);
    }
}
