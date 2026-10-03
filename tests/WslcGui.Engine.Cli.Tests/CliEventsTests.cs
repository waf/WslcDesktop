namespace WslcGui.Engine.Cli.Tests;

public class CliEventsTests
{
    [Fact]
    public void Every_captured_event_line_parses()
    {
        var lines = Fixtures.Read("events.txt").Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        var events = lines.Select(CliEventSource.Parse).ToList();

        Assert.All(events, Assert.NotNull);
        Assert.Contains(events, e => e!.Type == "container" && e.Action == "destroy");
    }

    [Fact]
    public void Parses_text_event_with_attributes()
    {
        var e = CliEventSource.Parse("2026-10-03T10:37:33.000000000+07:00 container start 1246fd70 (com.example=x, image=postgres:16-alpine, name=pg)\r");

        Assert.NotNull(e);
        Assert.Equal("container", e.Type);
        Assert.Equal("start", e.Action);
        Assert.Equal("1246fd70", e.ActorId);
        Assert.Equal("postgres:16-alpine", e.Attributes["image"]);
        Assert.Equal("pg", e.Attributes["name"]);
        Assert.Equal(new DateTimeOffset(2026, 10, 3, 10, 37, 33, TimeSpan.FromHours(7)), e.Time);
    }

    [Fact]
    public void Parses_stop_exit_code()
    {
        var e = CliEventSource.Parse("2026-10-03T10:37:33.000000000+07:00 container stop abc (exitCode=137, image=alpine, name=x)");

        Assert.Equal("137", e!.Attributes["exitCode"]);
    }

    [Fact]
    public void Parses_future_json_events()
    {
        var e = CliEventSource.Parse("""{"Type":"container","Action":"start","Actor":{"ID":"abc","Attributes":{"name":"web"}},"scope":"local","time":1790000000,"timeNano":1790000000123456789}""");

        Assert.NotNull(e);
        Assert.Equal("start", e.Action);
        Assert.Equal("abc", e.ActorId);
        Assert.Equal("web", e.Attributes["name"]);
        Assert.Equal(DateTimeOffset.FromUnixTimeSeconds(1790000000).AddTicks(1234567), e.Time);
    }

    [Theory]
    [InlineData("")]
    [InlineData("garbage")]
    [InlineData("{not json")]
    public void Unparseable_lines_are_skipped(string line) => Assert.Null(CliEventSource.Parse(line));

    [Fact]
    public async Task Watch_streams_parsed_events()
    {
        var cli = new FakeCliRunner().Returns("events",
            "2026-10-03T10:37:33.000000000+07:00 container create a (name=x)\r\n2026-10-03T10:37:34.000000000+07:00 container start a (name=x)\r\n");

        var actions = new List<string>();
        await foreach (var e in new CliEventSource(cli).WatchAsync(TestContext.Current.CancellationToken))
        {
            actions.Add(e.Action);
        }

        Assert.Equal(["create", "start"], actions);
    }
}

public class CliCommandLineTests
{
    [Theory]
    [InlineData("alpine", "alpine")]
    [InlineData("", "\"\"")]
    [InlineData("pa ss", "\"pa ss\"")]
    [InlineData("say \"hi\"", "\"say \\\"hi\\\"\"")]
    [InlineData(@"C:\data:/data", @"C:\data:/data")]
    [InlineData(@"C:\my data\", "\"C:\\my data\\\\\"")]
    public void Quotes_arguments(string argument, string expected) => Assert.Equal(expected, CliCommandLine.Quote(argument));

    [Fact]
    public void Describes_run_command()
    {
        var spec = new RunSpec("postgres:16-alpine")
        {
            Name = "db",
            Environment = new Dictionary<string, string> { ["POSTGRES_PASSWORD"] = "pa ss" },
            Ports = [new PortMapping(5432, 15432, null, PortProtocol.Tcp)],
        };

        Assert.Equal(
            "wslc container run --detach --name db --env \"POSTGRES_PASSWORD=pa ss\" --publish 15432:5432 postgres:16-alpine",
            CliEngine.DescribeRun(spec));
    }
}
