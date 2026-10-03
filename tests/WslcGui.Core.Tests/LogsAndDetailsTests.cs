using System.Runtime.CompilerServices;
using System.Threading.Channels;

using Microsoft.Extensions.Time.Testing;

using WslcGui.Engine;

namespace WslcGui.Core.Tests;

public class AnsiTextTests
{
    [Theory]
    [InlineData("plain", "plain")]
    [InlineData("\x1b[31mred\x1b[0m text", "red text")]
    [InlineData("\x1b[1;38;5;208mbold orange\x1b[m", "bold orange")]
    [InlineData("\x1b]0;title\u0007after", "after")]
    [InlineData("a\x1b[2Kb\x1b[1Ac", "abc")]
    public void Strips_escape_sequences(string input, string expected) => Assert.Equal(expected, AnsiText.Strip(input));
}

public class LogsViewModelTests
{
    private static readonly DateTimeOffset T0 = new(2026, 10, 3, 3, 0, 0, TimeSpan.Zero);
    private readonly FakeTimeProvider _time = new(T0);
    private readonly FakeLogs _logs = new();

    private static LogLine Line(string text, int second, LogOrigin origin = LogOrigin.StdOut) => new(text, T0.AddSeconds(second), origin);

    [Fact]
    public async Task Initial_history_is_sorted_by_timestamp_across_streams()
    {
        using var vm = new LogsViewModel(_logs, "web", _time);
        var appended = new List<string>();
        vm.TextAppended += appended.Add;
        vm.Start();

        // History: all stdout first, then all stderr (as wslc sends it).
        await _logs.SendAsync(Line("out 1", 1), Line("out 3", 3), Line("err 2", 2, LogOrigin.StdErr));
        await AdvanceUntil(() => appended.Count > 0, LogsViewModel.InitialBatchDelay);

        Assert.Equal("out 1\nerr 2\nout 3\n", Assert.Single(appended));
    }

    [Fact]
    public async Task Filter_and_timestamps_rerender_everything()
    {
        using var vm = new LogsViewModel(_logs, "web", _time);
        string? reset = null;
        vm.TextReset += text => reset = text;
        vm.Start();
        await _logs.SendAsync(Line("GET /index", 1), Line("POST /login", 2));
        await AdvanceUntil(() => vm.LineCount == 2, LogsViewModel.InitialBatchDelay);

        vm.Filter = "post";
        Assert.Equal("POST /login\n", reset);

        vm.Filter = "";
        vm.ShowTimestamps = true;
        Assert.Contains("  GET /index\n", reset, StringComparison.Ordinal);
        Assert.Matches(@"^\d{4}-\d\d-\d\d \d\d:\d\d:\d\d\.\d{3}  GET", reset);
    }

    [Fact]
    public async Task Paused_view_is_not_updated_until_resumed()
    {
        using var vm = new LogsViewModel(_logs, "web", _time);
        var appended = new List<string>();
        string? reset = null;
        vm.TextAppended += appended.Add;
        vm.TextReset += text => reset = text;
        vm.Start();
        vm.IsPaused = true;

        await _logs.SendAsync(Line("one", 1));
        await AdvanceUntil(() => vm.LineCount == 1, LogsViewModel.InitialBatchDelay);
        Assert.Empty(appended);

        vm.IsPaused = false;
        Assert.Equal("one\n", reset);
    }

    [Fact]
    public async Task Ended_stream_says_container_stopped()
    {
        using var vm = new LogsViewModel(_logs, "web", _time);
        vm.Start();
        await _logs.SendAsync(Line("bye", 1));
        _logs.Complete();

        await AdvanceUntil(() => !vm.IsStreaming, TimeSpan.Zero);

        Assert.Equal(1, vm.LineCount);
        Assert.Contains("stopped", vm.StatusText, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Stream_error_is_shown()
    {
        using var vm = new LogsViewModel(_logs, "web", _time);
        vm.Start();
        _logs.Fail(new EngineException(EngineErrorKind.NotFound, "Container 'web' not found."));

        await AdvanceUntil(() => !vm.IsStreaming, TimeSpan.Zero);

        Assert.Equal("Container 'web' not found.", vm.StatusText);
    }

    [Fact]
    public async Task Keeps_at_most_max_lines()
    {
        using var vm = new LogsViewModel(_logs, "web", _time);
        vm.Start();
        await _logs.SendAsync(Enumerable.Range(0, LogsViewModel.MaxLines + 1).Select(i => Line($"line {i}", i)).ToArray());

        await AdvanceUntil(() => vm.LineCount > 0, LogsViewModel.InitialBatchDelay);

        Assert.InRange(vm.LineCount, 1, LogsViewModel.MaxLines);
        Assert.EndsWith($"line {LogsViewModel.MaxLines}\n", vm.GetText(), StringComparison.Ordinal);
    }

    private async Task AdvanceUntil(Func<bool> condition, TimeSpan step)
    {
        for (var i = 0; i < 300 && !condition(); i++)
        {
            await Task.Delay(5, TestContext.Current.CancellationToken);
            _time.Advance(step);
        }

        Assert.True(condition(), "Condition not reached.");
    }

    internal sealed class FakeLogs : ILogSource
    {
        private readonly Channel<LogLine> _channel = Channel.CreateUnbounded<LogLine>();

        public List<string> Opened { get; } = [];

        public async Task SendAsync(params LogLine[] lines)
        {
            foreach (var line in lines)
            {
                await _channel.Writer.WriteAsync(line);
            }
        }

        public void Complete() => _channel.Writer.TryComplete();

        public void Fail(Exception error) => _channel.Writer.TryComplete(error);

        public async IAsyncEnumerable<LogLine> ReadAsync(string containerId, LogOptions options, [EnumeratorCancellation] CancellationToken cancellationToken)
        {
            Opened.Add(containerId);
            await foreach (var line in _channel.Reader.ReadAllAsync(cancellationToken))
            {
                yield return line;
            }
        }
    }
}

public class ContainerDetailsViewModelTests
{
    private readonly FakeEngine _engine = new();
    private readonly FakeUi _ui = new();
    private readonly FakeExec _exec = new();
    private readonly FakeTerminal _terminal = new();

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private async Task<(ContainersViewModel List, ContainerDetailsViewModel Details)> OpenAsync(ContainerState state = ContainerState.Running)
    {
        _engine.Containers.Add(FakeEngine.Container("abc", "web", state));
        var list = new ContainersViewModel(_engine, _engine, _engine, _ui);
        await list.RefreshAsync(cancellationToken: Ct);
        var stats = new StatsMonitor(new NoStats(), _engine, () => false);
        var details = new ContainerDetailsViewModel(list.Items[0], list, _engine, new LogsViewModelTests.FakeLogs(), _exec, _terminal, new NoFiles(), stats, _ui);
        return (list, details);
    }

    [Fact]
    public async Task Follows_row_updates_and_notices_removal()
    {
        var (list, details) = await OpenAsync();
        using var _ = list;
        using var __ = details;

        _engine.Containers[0] = _engine.Containers[0] with { State = ContainerState.Exited, Status = "Exited (0)" };
        await list.RefreshAsync(cancellationToken: Ct);
        Assert.Equal(ContainerState.Exited, details.Row.State);
        Assert.False(details.IsRemoved);

        list.SearchText = "something-else";
        Assert.False(details.IsRemoved);

        _engine.Containers.Clear();
        await list.RefreshAsync(cancellationToken: Ct);
        Assert.True(details.IsRemoved);
    }

    [Fact]
    public async Task Exec_appends_command_output_and_exit_code()
    {
        var (list, details) = await OpenAsync();
        using var _ = list;
        using var __ = details;
        _exec.Result = new ExecResult(2, "out\n", "\x1b[31merr\x1b[0m");

        details.ExecCommand = "ls -la '/my dir'";
        await details.RunExecAsync();

        Assert.Equal(["ls", "-la", "/my dir"], _exec.LastCommand);
        Assert.Equal("$ ls -la '/my dir'\nout\nerr\n[exit code 2]\n", details.ExecTranscript);
        Assert.Equal(string.Empty, details.ExecCommand);
    }

    [Fact]
    public async Task Exec_error_is_shown_in_transcript()
    {
        var (list, details) = await OpenAsync();
        using var _ = list;
        using var __ = details;
        _exec.Error = new EngineException(EngineErrorKind.NotRunning, "Container 'web' is not running.");

        details.ExecCommand = "ls";
        await details.RunExecAsync();

        Assert.EndsWith("Container 'web' is not running.\n", details.ExecTranscript, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Terminal_opens_by_name()
    {
        var (list, details) = await OpenAsync();
        using var _ = list;
        using var __ = details;

        details.OpenTerminal();

        Assert.Equal(["web"], _terminal.Launched);
    }

    private sealed class FakeExec : IExecService
    {
        public ExecResult Result { get; set; } = new(0, string.Empty, string.Empty);
        public EngineException? Error { get; set; }
        public IReadOnlyList<string>? LastCommand { get; private set; }

        public Task<ExecResult> ExecAsync(string containerId, IReadOnlyList<string> command, CancellationToken cancellationToken = default)
        {
            LastCommand = command;
            return Error is { } error ? Task.FromException<ExecResult>(error) : Task.FromResult(Result);
        }
    }

    private sealed class NoStats : IStatsSource
    {
        public Task<IReadOnlyList<ContainerStats>> SnapshotAsync(IReadOnlyList<string> containerIds, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<ContainerStats>>([]);
    }

    private sealed class NoFiles : IContainerFiles
    {
        public Task<IReadOnlyList<ContainerFileEntry>> ListDirectoryAsync(string containerId, string path, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<IContainerFileSnapshot> OpenSnapshotAsync(string containerId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task DownloadAsync(string containerId, string containerPath, string localDirectory, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task UploadAsync(string containerId, string localPath, string containerDirectory, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<byte[]> ReadFileStartAsync(string containerId, string containerPath, int maxBytes, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }

    private sealed class FakeTerminal : IExternalTerminalLauncher
    {
        public List<string> Launched { get; } = [];

        public void Launch(string containerId, string? shell) => Launched.Add(containerId);
    }
}
