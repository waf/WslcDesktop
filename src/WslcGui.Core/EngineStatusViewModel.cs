using WslcGui.Engine;

namespace WslcGui.Core;

/// <summary>Engine availability and version, shown in the status bar and on the startup screen.</summary>
public sealed class EngineStatusViewModel(IEngineInfo engineInfo) : ObservableObject
{
    private EngineHealthStatus? _status;
    private string _statusText = "Connecting to WSLC…";
    private string? _version;

    /// <summary>Null until the first check completes.</summary>
    public EngineHealthStatus? Status
    {
        get => _status;
        private set => SetProperty(ref _status, value);
    }

    public string StatusText
    {
        get => _statusText;
        private set => SetProperty(ref _statusText, value);
    }

    public string? Version
    {
        get => _version;
        private set => SetProperty(ref _version, value);
    }

    public async Task RefreshAsync(CancellationToken cancellationToken = default)
    {
        var health = await engineInfo.CheckHealthAsync(cancellationToken);
        Status = health.Status;
        Version = health.Version?.ClientVersion;
        StatusText = health.Status switch
        {
            EngineHealthStatus.Ready => $"WSLC {Version} — ready",
            EngineHealthStatus.NotInstalled => "WSLC is not installed. Run 'wsl --update' to install it.",
            _ => $"WSLC error: {health.Message}",
        };
    }
}
