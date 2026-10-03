using System.Text;

using WslcGui.Engine;

namespace WslcGui.Core;

/// <summary>The container details view: header state, logs, inspect, one-shot exec and terminal.</summary>
public sealed class ContainerDetailsViewModel : ObservableObject, IDisposable
{
    private readonly ContainersViewModel _containers;
    private readonly IContainerQueries _queries;
    private readonly IExecService _exec;
    private readonly IExternalTerminalLauncher _terminal;
    private ContainerRow _row;
    private bool _isRemoved;
    private string? _inspectJson;
    private bool _isInspectLoading;
    private string _execCommand = string.Empty;
    private bool _isExecBusy;
    private readonly StringBuilder _execTranscript = new();

    public ContainerDetailsViewModel(
        ContainerRow row,
        ContainersViewModel containers,
        IContainerQueries queries,
        ILogSource logs,
        IExecService exec,
        IExternalTerminalLauncher terminal,
        TimeProvider? timeProvider = null)
    {
        _row = row;
        _containers = containers;
        _queries = queries;
        _exec = exec;
        _terminal = terminal;
        Logs = new LogsViewModel(logs, row.Id, timeProvider);
        _containers.RowsChanged += OnContainersChanged;
    }

    public ContainerRow Row
    {
        get => _row;
        private set => SetProperty(ref _row, value);
    }

    /// <summary>The container no longer exists (the view should go back to the list).</summary>
    public bool IsRemoved
    {
        get => _isRemoved;
        private set => SetProperty(ref _isRemoved, value);
    }

    public LogsViewModel Logs { get; }

    public string? InspectJson
    {
        get => _inspectJson;
        private set => SetProperty(ref _inspectJson, value);
    }

    public bool IsInspectLoading
    {
        get => _isInspectLoading;
        private set => SetProperty(ref _isInspectLoading, value);
    }

    public string ExecCommand
    {
        get => _execCommand;
        set => SetProperty(ref _execCommand, value ?? string.Empty);
    }

    public bool IsExecBusy
    {
        get => _isExecBusy;
        private set => SetProperty(ref _isExecBusy, value);
    }

    /// <summary>All exec commands and their output so far.</summary>
    public string ExecTranscript => _execTranscript.ToString();

    public Task StartAsync() => _containers.StartAsync([Row]);

    public Task StopAsync() => _containers.StopAsync([Row]);

    public Task RestartAsync() => _containers.RestartAsync([Row]);

    public Task RemoveAsync() => _containers.RemoveAsync([Row]);

    /// <summary>Opens an interactive shell in an external terminal window.</summary>
    public void OpenTerminal()
    {
        try
        {
            _terminal.Launch(Row.Name.Length > 0 ? Row.Name : Row.Id, shell: null);
        }
        catch (EngineException ex)
        {
            _containers.ReportError("Couldn't open a terminal", ex);
        }
    }

    public async Task LoadInspectAsync()
    {
        IsInspectLoading = true;
        try
        {
            InspectJson = await _queries.InspectAsync(Row.Id);
        }
        catch (EngineException ex)
        {
            InspectJson = ex.Message;
        }
        finally
        {
            IsInspectLoading = false;
        }
    }

    /// <summary>Runs <see cref="ExecCommand"/> in the container and appends it and its output to <see cref="ExecTranscript"/>.</summary>
    public async Task RunExecAsync()
    {
        var text = ExecCommand.Trim();
        if (text.Length == 0 || IsExecBusy)
        {
            return;
        }

        var command = CommandLineSplitter.TrySplit(text, out var error);
        _execTranscript.Append("$ ").Append(text).Append('\n');
        if (command is null)
        {
            AppendTranscript(error + "\n");
            return;
        }

        IsExecBusy = true;
        try
        {
            var result = await _exec.ExecAsync(Row.Id, command);
            ExecCommand = string.Empty;
            var output = new StringBuilder();
            output.Append(AnsiText.Strip(result.StdOut));
            output.Append(AnsiText.Strip(result.StdErr));
            if (output.Length > 0 && output[^1] != '\n')
            {
                output.Append('\n');
            }

            if (result.ExitCode != 0)
            {
                output.Append("[exit code ").Append(result.ExitCode).Append("]\n");
            }

            AppendTranscript(output.ToString());
        }
        catch (EngineException ex)
        {
            AppendTranscript(ex.Message + "\n");
        }
        finally
        {
            IsExecBusy = false;
        }
    }

    public void Dispose()
    {
        _containers.RowsChanged -= OnContainersChanged;
        Logs.Dispose();
    }

    private void AppendTranscript(string text)
    {
        _execTranscript.Append(text);
        OnPropertyChanged(nameof(ExecTranscript));
    }

    private void OnContainersChanged()
    {
        var current = _containers.Find(Row.Id);
        if (current is null)
        {
            if (_containers.IsKnownRemoved(Row.Id))
            {
                IsRemoved = true;
                Logs.Stop();
            }

            return;
        }

        var wasRunning = Row.IsRunning;
        Row = current;

        // logs -f ends when the container stops; pick the stream up again when it restarts.
        if (!wasRunning && current.IsRunning && !Logs.IsStreaming)
        {
            Logs.Start();
        }
    }
}
