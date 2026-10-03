namespace WslcGui.Engine.Cli.Tests;

public class CliLogsTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public void Parses_captured_timestamped_lines()
    {
        var line = Fixtures.Read("logs-t.txt").Split('\n')[0];

        var parsed = CliLogSource.Parse(line, LogOrigin.StdOut);

        Assert.NotNull(parsed);
        Assert.StartsWith("out line 103", parsed.Text, StringComparison.Ordinal);
        Assert.Equal(new DateTimeOffset(2026, 10, 3, 3, 39, 17, TimeSpan.Zero).AddTicks(3959732), parsed.Timestamp);
    }

    [Theory]
    [InlineData("Container 'x' not found.")]
    [InlineData("Error code: WSLC_E_CONTAINER_NOT_FOUND")]
    [InlineData("")]
    public void Lines_without_timestamp_are_not_log_lines(string line) => Assert.Null(CliLogSource.Parse(line, LogOrigin.StdErr));

    [Fact]
    public void Empty_message_line_is_kept()
    {
        var parsed = CliLogSource.Parse("2026-10-03T03:39:17.395973225Z ", LogOrigin.StdOut);

        Assert.Equal(string.Empty, parsed!.Text);
    }

    [Fact]
    public async Task Read_streams_both_origins_and_follows()
    {
        var cli = new FakeCliRunner();
        cli.Streams["container logs --timestamps --follow --tail 100 web"] =
        [
            new(CliOutputKind.StdOut, "2026-10-03T03:39:17.000000001Z hello"),
            new(CliOutputKind.StdErr, "2026-10-03T03:39:18.000000001Z oops"),
            new(CliOutputKind.Exit, string.Empty, 0),
        ];

        var lines = new List<LogLine>();
        await foreach (var line in new CliLogSource(cli).ReadAsync("web", new LogOptions(Follow: true, Tail: 100), Ct))
        {
            lines.Add(line);
        }

        Assert.Equal(["hello", "oops"], lines.Select(l => l.Text));
        Assert.Equal([LogOrigin.StdOut, LogOrigin.StdErr], lines.Select(l => l.Origin));
    }

    [Fact]
    public async Task Read_raises_wslc_errors_instead_of_yielding_them()
    {
        var cli = new FakeCliRunner();
        cli.Streams["container logs --timestamps nope"] =
        [
            new(CliOutputKind.StdErr, "Container 'nope' not found."),
            new(CliOutputKind.StdErr, "Error code: WSLC_E_CONTAINER_NOT_FOUND"),
            new(CliOutputKind.Exit, string.Empty, 1),
        ];

        var ex = await Assert.ThrowsAsync<EngineException>(async () =>
        {
            await foreach (var _ in new CliLogSource(cli).ReadAsync("nope", new LogOptions(Follow: false, Tail: null), Ct))
            {
            }
        });

        Assert.Equal(EngineErrorKind.NotFound, ex.Kind);
        Assert.Equal("Container 'nope' not found.", ex.Message);
    }
}

public class CliExecTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Command_exit_code_is_a_result_not_an_error()
    {
        var captured = Fixtures.ReadResult("error-exec-nonzero-exit.txt");
        var cli = new FakeCliRunner().Returns("container exec web sh", captured.StdOut, captured.ExitCode, captured.StdErr);

        var result = await new CliExecService(cli).ExecAsync("web", ["sh"], Ct);

        Assert.Equal(7, result.ExitCode);
        Assert.Equal("to-out", result.StdOut.Trim());
        Assert.Equal("to-err", result.StdErr.Trim());
    }

    [Fact]
    public async Task Stopped_container_is_an_error()
    {
        var captured = Fixtures.ReadResult("error-exec-stopped.txt");
        var cli = new FakeCliRunner().Returns("container exec web echo", captured.StdOut, captured.ExitCode, captured.StdErr);

        var ex = await Assert.ThrowsAsync<EngineException>(() => new CliExecService(cli).ExecAsync("web", ["echo"], Ct));

        Assert.Equal(EngineErrorKind.NotRunning, ex.Kind);
    }
}

public class CliExternalTerminalTests
{
    [Fact]
    public void Opens_a_windows_terminal_tab_running_wslc_exec()
    {
        var cli = new FakeCliRunner { GlobalArguments = ["--session", "s"] };

        var id = string.Concat(Enumerable.Repeat("0123456789abcdef", 4));
        new CliExternalTerminal(cli, () => @"C:\wt.exe").Launch(id, shell: null);

        var (executable, arguments) = Assert.Single(cli.Launched);
        Assert.Equal(@"C:\wt.exe", executable);
        Assert.Equal(
            ["new-tab", "--title", "0123456789ab", cli.ExecutablePath!, "--session", "s", "container", "exec", "--interactive", "--tty",
                id, "sh", "-c", CliExternalTerminal.DefaultShellScript],
            arguments);
        Assert.DoesNotContain(';', CliExternalTerminal.DefaultShellScript);
    }

    [Fact]
    public void Names_are_used_as_the_tab_title_unchanged()
    {
        var cli = new FakeCliRunner();

        new CliExternalTerminal(cli, () => @"C:\wt.exe").Launch("wslcgui-demo-logger", shell: null);

        Assert.Equal("wslcgui-demo-logger", Assert.Single(cli.Launched).Arguments[2]);
    }

    [Fact]
    public void Falls_back_to_a_console_window()
    {
        var cli = new FakeCliRunner();

        new CliExternalTerminal(cli, () => null).Launch("web", shell: "/bin/bash");

        var (executable, arguments) = Assert.Single(cli.Launched);
        Assert.Equal(cli.ExecutablePath, executable);
        Assert.Equal(["container", "exec", "--interactive", "--tty", "web", "/bin/bash"], arguments);
    }
}
