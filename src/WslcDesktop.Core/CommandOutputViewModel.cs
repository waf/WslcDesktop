using System.Text;

using WslcDesktop.Engine;

namespace WslcDesktop.Core;

/// <summary>A long-running engine operation (build, push) whose output lines are shown live, with cancel.</summary>
public sealed class CommandOutputViewModel : ObservableObject, IDisposable
{
    /// <summary>Output kept in memory; the start is dropped beyond this.</summary>
    public const int MaxOutputChars = 1_000_000;

    private readonly StringBuilder _output = new();
    private CancellationTokenSource? _cancellation;
    private bool _isBusy;
    private string? _status;
    private bool? _succeeded;

    /// <summary>Raised with each output line (on the UI thread).</summary>
    public event Action<string>? LineAdded;

    public bool IsBusy { get => _isBusy; private set => SetProperty(ref _isBusy, value); }

    /// <summary>"Building…", "Done.", an error message, ...</summary>
    public string? Status { get => _status; private set => SetProperty(ref _status, value); }

    /// <summary>Null until finished.</summary>
    public bool? Succeeded { get => _succeeded; private set => SetProperty(ref _succeeded, value); }

    public string Output => _output.ToString();

    /// <summary>Runs the operation; returns whether it succeeded.</summary>
    public async Task<bool> RunAsync(string busyText, string doneText, Func<IProgress<string>, CancellationToken, Task> operation)
    {
        _cancellation = new CancellationTokenSource();
        IsBusy = true;
        Succeeded = null;
        Status = busyText;
        try
        {
            await operation(new OrderedProgress<string>(AddLine), _cancellation.Token);
            Status = doneText;
            Succeeded = true;
        }
        catch (OperationCanceledException)
        {
            Status = "Cancelled.";
            Succeeded = false;
        }
        catch (EngineException ex)
        {
            Status = ex.Message;
            Succeeded = false;
        }
        finally
        {
            IsBusy = false;
            _cancellation.Dispose();
            _cancellation = null;
        }

        return Succeeded == true;
    }

    public void Cancel() => _cancellation?.Cancel();

    public void Dispose() => _cancellation?.Dispose();

    private void AddLine(string line)
    {
        var text = AnsiText.Strip(line);
        _output.Append(text).Append('\n');
        if (_output.Length > MaxOutputChars)
        {
            _output.Remove(0, _output.Length - (MaxOutputChars * 9 / 10));
        }

        LineAdded?.Invoke(text);
    }
}

/// <summary>The "Build an image" form.</summary>
public sealed class BuildImageViewModel(IImageService images) : ObservableObject, IDisposable
{
    private string _contextDirectory = string.Empty;
    private string _dockerfile = string.Empty;
    private string _tag = string.Empty;
    private bool _noCache;
    private bool _pull;
    private string? _error;

    public string ContextDirectory { get => _contextDirectory; set => SetProperty(ref _contextDirectory, value ?? string.Empty); }

    /// <summary>Optional; the context's Dockerfile when empty.</summary>
    public string Dockerfile { get => _dockerfile; set => SetProperty(ref _dockerfile, value ?? string.Empty); }

    public string Tag { get => _tag; set => SetProperty(ref _tag, value ?? string.Empty); }

    public bool NoCache { get => _noCache; set => SetProperty(ref _noCache, value); }

    public bool Pull { get => _pull; set => SetProperty(ref _pull, value); }

    /// <summary>Build arguments (KEY=VALUE).</summary>
    public System.Collections.ObjectModel.ObservableCollection<EnvironmentEntry> BuildArgs { get; } = [];

    public string? Error { get => _error; private set => SetProperty(ref _error, value); }

    public CommandOutputViewModel Output { get; } = new();

    public BuildSpec? TryBuildSpec()
    {
        var context = _contextDirectory.Trim();
        if (context.Length == 0)
        {
            Error = "Choose the folder to build (the build context).";
            return null;
        }

        var tag = _tag.Trim();
        if (tag.Any(char.IsWhiteSpace) || tag.Any(char.IsUpper))
        {
            Error = "The tag must be lowercase without spaces, for example myapp:dev.";
            return null;
        }

        var args = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var entry in BuildArgs.Where(e => e.Key.Trim().Length > 0))
        {
            args[entry.Key.Trim()] = entry.Value;
        }

        Error = null;
        return new BuildSpec(context)
        {
            Dockerfile = _dockerfile.Trim().Length > 0 ? _dockerfile.Trim() : null,
            Tag = tag.Length > 0 ? tag : null,
            BuildArgs = args,
            NoCache = _noCache,
            Pull = _pull,
        };
    }

    public async Task<bool> BuildAsync()
    {
        if (TryBuildSpec() is not { } spec)
        {
            return false;
        }

        return await Output.RunAsync("Building…", spec.Tag is { } tag ? $"Built {tag}." : "Built.", (progress, ct) => images.BuildAsync(spec, progress, ct));
    }

    public void Dispose() => Output.Dispose();
}

/// <summary>The "Sign in to a registry" form.</summary>
public sealed class RegistryLoginViewModel(IRegistryService registry) : ObservableObject
{
    private string _server = string.Empty;
    private string _username = string.Empty;
    private string _password = string.Empty;
    private bool _isBusy;
    private string? _error;

    /// <summary>Empty for Docker Hub.</summary>
    public string Server { get => _server; set => SetProperty(ref _server, value ?? string.Empty); }
    public string Username { get => _username; set => SetProperty(ref _username, value ?? string.Empty); }
    public string Password { get => _password; set => SetProperty(ref _password, value ?? string.Empty); }
    public bool IsBusy { get => _isBusy; private set => SetProperty(ref _isBusy, value); }
    public string? Error { get => _error; private set => SetProperty(ref _error, value); }

    public async Task<bool> LoginAsync()
    {
        if (_username.Trim().Length == 0 || _password.Length == 0)
        {
            Error = "Enter a username and a password or access token.";
            return false;
        }

        IsBusy = true;
        Error = null;
        try
        {
            await registry.LoginAsync(_server.Trim().Length > 0 ? _server.Trim() : null, _username.Trim(), _password);
            return true;
        }
        catch (EngineException ex)
        {
            Error = ex.Message;
            return false;
        }
        finally
        {
            IsBusy = false;
            Password = string.Empty;
        }
    }
}
