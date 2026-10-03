namespace WslcDesktop.Engine.Cli.Tests;

public class CliContainersTests
{
    private const string ListAll = "container list --no-trunc --format json --all";

    [Fact]
    public async Task List_maps_captured_output()
    {
        var cli = new FakeCliRunner().Returns(ListAll, Fixtures.Read("container-list-a.no-trunc.json"));

        var containers = await new CliContainerQueries(cli).ListAsync(includeStopped: true, TestContext.Current.CancellationToken);

        Assert.NotEmpty(containers);
        Assert.All(containers, c => Assert.Equal(64, c.Id.Length));

        var exited = Assert.Single(containers, c => c.Name == "wslcgui-s1-exited");
        Assert.Equal(ContainerState.Exited, exited.State);
        Assert.Equal("alpine:latest", exited.Image);
        Assert.Equal("sh -c 'echo hello-from-exited; echo bye >&2; exit 3'", exited.Command);
        Assert.StartsWith("Exited (3)", exited.Status, StringComparison.Ordinal);
        Assert.Equal(TimeSpan.FromHours(7), exited.CreatedAt?.Offset);

        Assert.Contains(containers, c => c.State == ContainerState.Running
            && c.Ports.Contains(new PortMapping(5432, 15432, "127.0.0.1", PortProtocol.Tcp)));
    }

    [Fact]
    public async Task List_running_only_omits_all_flag()
    {
        var cli = new FakeCliRunner().Returns("container list --no-trunc --format json", "");

        var containers = await new CliContainerQueries(cli).ListAsync(includeStopped: false, TestContext.Current.CancellationToken);

        Assert.Empty(containers);
    }

    [Fact]
    public void Run_arguments_cover_the_spec()
    {
        var spec = new RunSpec("postgres:16-alpine")
        {
            Name = "db",
            Command = ["postgres", "-c", "log_statement=all"],
            Environment = new Dictionary<string, string> { ["POSTGRES_PASSWORD"] = "pa ss" },
            Ports = [new PortMapping(5432, 15432, null, PortProtocol.Tcp), new PortMapping(53, 5353, "::1", PortProtocol.Udp)],
            Mounts = [new VolumeMount(@"C:\data", "/var/lib/postgresql/data", ReadOnly: false), new VolumeMount("cache", "/cache", ReadOnly: true)],
            Network = "backend",
            AutoRemove = true,
        };

        var arguments = CliContainerLifecycle.BuildCreateArguments("run", spec);

        Assert.Equal(
            [
                "container", "run", "--detach",
                "--name", "db",
                "--env", "POSTGRES_PASSWORD=pa ss",
                "--publish", "15432:5432",
                "--publish", "[::1]:5353:53/udp",
                "--volume", @"C:\data:/var/lib/postgresql/data",
                "--volume", "cache:/cache:ro",
                "--network", "backend",
                "--rm",
                "postgres:16-alpine", "postgres", "-c", "log_statement=all",
            ],
            arguments);
    }

    [Fact]
    public async Task Run_returns_the_printed_container_id()
    {
        var id = new string('a', 64);
        var cli = new FakeCliRunner().Returns("container run --detach alpine", $"{id}\r\n");

        var result = await new CliContainerLifecycle(cli).RunAsync(new RunSpec("alpine"), TestContext.Current.CancellationToken);

        Assert.Equal(id, result);
    }

    [Fact]
    public async Task Remove_running_container_surfaces_conflict()
    {
        var captured = Fixtures.ReadResult("error-container-remove-running.txt");
        var cli = new FakeCliRunner().Returns("container remove web", captured.StdOut, captured.ExitCode, captured.StdErr);

        var ex = await Assert.ThrowsAsync<EngineException>(() => new CliContainerLifecycle(cli).RemoveAsync("web", force: false, TestContext.Current.CancellationToken));

        Assert.Equal(EngineErrorKind.Conflict, ex.Kind);
    }
}
