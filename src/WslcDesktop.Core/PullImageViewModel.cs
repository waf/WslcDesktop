using WslcDesktop.Engine;

namespace WslcDesktop.Core;

/// <summary>The "Pull image" form.</summary>
public sealed class PullImageViewModel(IImageService images) : ObservableObject, IDisposable
{
    private string _reference = string.Empty;
    private bool _isBusy;
    private string? _status;
    private string? _error;
    private CancellationTokenSource? _cancellation;

    /// <summary>For example "alpine", "redis:7-alpine" or "ghcr.io/owner/image:tag".</summary>
    public string Reference { get => _reference; set => SetProperty(ref _reference, value ?? string.Empty); }

    public bool IsBusy { get => _isBusy; private set => SetProperty(ref _isBusy, value); }

    public string? Status { get => _status; private set => SetProperty(ref _status, value); }

    public string? Error { get => _error; private set => SetProperty(ref _error, value); }

    /// <returns>True if the image was pulled.</returns>
    public async Task<bool> PullAsync()
    {
        var reference = _reference.Trim();
        if (reference.Length == 0 || reference.Any(char.IsWhiteSpace))
        {
            Error = "Enter an image name, for example 'alpine' or 'redis:7-alpine'.";
            return false;
        }

        _cancellation = new CancellationTokenSource();
        IsBusy = true;
        Error = null;
        Status = $"Pulling {reference}…";
        try
        {
            var progress = new Progress<PullProgress>(p =>
                Status = p.LayerId is null ? p.Status : $"{p.LayerId}: {p.Status}");
            await images.PullAsync(reference, progress, _cancellation.Token);
            Status = $"Pulled {reference}.";
            return true;
        }
        catch (OperationCanceledException)
        {
            Status = "Cancelled.";
            return false;
        }
        catch (EngineException ex)
        {
            Status = null;
            Error = ex.Message;
            return false;
        }
        finally
        {
            IsBusy = false;
            _cancellation.Dispose();
            _cancellation = null;
        }
    }

    public void Cancel() => _cancellation?.Cancel();

    public void Dispose() => _cancellation?.Dispose();
}
