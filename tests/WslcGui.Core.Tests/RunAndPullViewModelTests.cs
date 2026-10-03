using WslcGui.Engine;

namespace WslcGui.Core.Tests;

public class CommandLineSplitterTests
{
    [Theory]
    [InlineData("", new string[0])]
    [InlineData("sleep infinity", new[] { "sleep", "infinity" })]
    [InlineData("  sh   -c  'echo hi; exit 3'  ", new[] { "sh", "-c", "echo hi; exit 3" })]
    [InlineData("echo \"say \\\"hi\\\"\" a\\ b", new[] { "echo", "say \"hi\"", "a b" })]
    [InlineData("echo '' x", new[] { "echo", "", "x" })]
    public void Splits_like_a_shell(string text, string[] expected)
    {
        var arguments = CommandLineSplitter.TrySplit(text, out var error);

        Assert.Null(error);
        Assert.Equal(expected, arguments);
    }

    [Fact]
    public void Unclosed_quote_is_an_error()
    {
        Assert.Null(CommandLineSplitter.TrySplit("sh -c 'echo", out var error));
        Assert.Contains("unclosed", error, StringComparison.Ordinal);
    }
}

public class RunContainerViewModelTests
{
    private readonly FakeEngine _engine = new();

    [Fact]
    public void Builds_spec_from_form_skipping_blank_rows()
    {
        var vm = new RunContainerViewModel(_engine, "postgres:16-alpine")
        {
            Name = "db",
            Command = "postgres -c 'log_statement=all'",
            AutoRemove = true,
        };
        vm.Environment.Add(new EnvironmentEntry { Key = "POSTGRES_PASSWORD", Value = "secret" });
        vm.Environment.Add(new EnvironmentEntry());
        vm.Ports.Add(new PortEntry { HostPort = "15432", ContainerPort = "5432" });
        vm.Ports.Add(new PortEntry { ContainerPort = "8080" });
        vm.Mounts.Add(new MountEntry { Source = @"C:\data", Target = "/data", ReadOnly = true });

        var spec = vm.TryBuildSpec(out var errors);

        Assert.Empty(errors);
        Assert.NotNull(spec);
        Assert.Equal("db", spec.Name);
        Assert.Equal(["postgres", "-c", "log_statement=all"], spec.Command);
        Assert.Equal("secret", spec.Environment["POSTGRES_PASSWORD"]);
        Assert.Single(spec.Environment);
        Assert.Equal([new PortMapping(5432, 15432, null, PortProtocol.Tcp), new PortMapping(8080, null, null, PortProtocol.Tcp)], spec.Ports);
        Assert.Equal([new VolumeMount(@"C:\data", "/data", true)], spec.Mounts);
        Assert.True(spec.AutoRemove);
    }

    [Fact]
    public void Reports_every_problem()
    {
        var vm = new RunContainerViewModel(_engine) { Name = "-bad", Command = "sh -c 'oops" };
        vm.Environment.Add(new EnvironmentEntry { Key = "A=B", Value = "x" });
        vm.Ports.Add(new PortEntry { HostPort = "99999", ContainerPort = "80" });
        vm.Mounts.Add(new MountEntry { Source = @"C:\data", Target = "data" });

        Assert.Null(vm.TryBuildSpec(out var errors));
        Assert.Equal(6, errors.Count);
    }

    [Fact]
    public void Preview_tracks_edits_including_row_changes()
    {
        var vm = new RunContainerViewModel(_engine, "alpine", describe: spec => $"run {spec.Image} {string.Join(',', spec.Ports.Select(p => p.ContainerPort))}");
        Assert.Equal("run alpine ", vm.CommandPreview);

        var port = new PortEntry { ContainerPort = "80" };
        vm.Ports.Add(port);
        Assert.Equal("run alpine 80", vm.CommandPreview);

        port.ContainerPort = "81";
        Assert.Equal("run alpine 81", vm.CommandPreview);

        vm.Image = "";
        Assert.Equal("Choose an image.", vm.CommandPreview);
    }

    [Fact]
    public async Task Run_returns_id_or_shows_engine_error()
    {
        var vm = new RunContainerViewModel(_engine, "alpine");
        Assert.Equal("id", await vm.RunAsync(TestContext.Current.CancellationToken));
        Assert.Equal(["run alpine"], _engine.Calls);

        _engine.Failures["run"] = new EngineException(EngineErrorKind.Conflict, "name in use");
        Assert.Null(await vm.RunAsync(TestContext.Current.CancellationToken));
        Assert.Equal("name in use", vm.Error);
        Assert.False(vm.IsBusy);
    }

    [Fact]
    public async Task Run_with_invalid_form_does_not_call_engine()
    {
        var vm = new RunContainerViewModel(_engine);

        Assert.Null(await vm.RunAsync(TestContext.Current.CancellationToken));

        Assert.Equal("Choose an image.", vm.Error);
        Assert.Empty(_engine.Calls);
    }
}

public class PullImageViewModelTests
{
    private readonly FakeEngine _engine = new();

    [Fact]
    public async Task Pulls_trimmed_reference()
    {
        using var vm = new PullImageViewModel(_engine) { Reference = "  redis:7-alpine " };

        Assert.True(await vm.PullAsync());

        Assert.Equal(["pull redis:7-alpine"], _engine.Calls);
        Assert.Equal("Pulled redis:7-alpine.", vm.Status);
    }

    [Theory]
    [InlineData("")]
    [InlineData("two words")]
    public async Task Rejects_invalid_reference(string reference)
    {
        using var vm = new PullImageViewModel(_engine) { Reference = reference };

        Assert.False(await vm.PullAsync());

        Assert.NotNull(vm.Error);
        Assert.Empty(_engine.Calls);
    }

    [Fact]
    public async Task Shows_engine_error()
    {
        _engine.Failures["pull"] = new EngineException(EngineErrorKind.NotFound, "pull access denied");
        using var vm = new PullImageViewModel(_engine) { Reference = "nope" };

        Assert.False(await vm.PullAsync());

        Assert.Equal("pull access denied", vm.Error);
    }
}
