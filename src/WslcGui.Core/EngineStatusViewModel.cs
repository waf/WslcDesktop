using WslcGui.Engine;

namespace WslcGui.Core;

/// <summary>Engine availability, version and VM state, shown in the status bar.</summary>
public sealed class EngineStatusViewModel(IEngineInfo engineInfo) : ObservableObject
{
    private EngineHealth? _health;
    private EngineRuntimeState _runtimeState;
    private string _statusText = "Connecting to WSLC…";

    /// <summary>Null until the first check completes.</summary>
    public EngineHealthStatus? Status => _health?.Status;

    public string? Version => _health?.Version?.ClientVersion;

    public EngineRuntimeState RuntimeState
    {
        get => _runtimeState;
        private set => SetProperty(ref _runtimeState, value);
    }

    public string StatusText
    {
        get => _statusText;
        private set => SetProperty(ref _statusText, value);
    }

    /// <summary>Checks the engine is installed and usable. Doesn't wake the VM.</summary>
    public async Task RefreshAsync(CancellationToken cancellationToken = default)
    {
        _health = await engineInfo.CheckHealthAsync(cancellationToken);
        OnPropertyChanged(nameof(Status));
        OnPropertyChanged(nameof(Version));
        await RefreshRuntimeStateAsync(cancellationToken);
    }

    /// <summary>Updates whether the VM is running. Cheap and doesn't wake the VM, so it can run on every poll tick.</summary>
    public async Task RefreshRuntimeStateAsync(CancellationToken cancellationToken = default)
    {
        RuntimeState = await engineInfo.GetRuntimeStateAsync(cancellationToken);
        StatusText = _health switch
        {
            null => "Connecting to WSLC…",
            { Status: EngineHealthStatus.NotInstalled } => "WSLC is not installed. Run 'wsl --update' to install it.",
            { Status: EngineHealthStatus.Error } h => $"WSLC error: {h.Message}",
            _ => RuntimeState switch
            {
                EngineRuntimeState.Running => $"WSLC {Version} — engine running",
                EngineRuntimeState.Idle => $"WSLC {Version} — engine idle (starts on demand)",
                _ => $"WSLC {Version} — ready",
            },
        };
    }
}
