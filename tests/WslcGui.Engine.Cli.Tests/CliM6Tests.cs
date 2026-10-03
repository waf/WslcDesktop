namespace WslcGui.Engine.Cli.Tests;

public class CliVolumesAndNetworksTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Lists_volumes_and_flags_anonymous_ones()
    {
        var cli = new FakeCliRunner().Returns("volume list --format json", Fixtures.Read("volume-list.json"));

        var volumes = await new CliVolumeService(cli).ListAsync(Ct);

        var named = Assert.Single(volumes, v => v.Name == "wslcgui-s1-vol");
        Assert.Equal(("guest", false), (named.Driver, named.IsAnonymous));
        Assert.Contains(volumes, v => v.IsAnonymous);
    }

    [Fact]
    public async Task Lists_networks_with_nanosecond_utc_timestamps()
    {
        var cli = new FakeCliRunner().Returns("network list --no-trunc --format json", Fixtures.Read("network-list.json"));

        var networks = await new CliNetworkService(cli).ListAsync(Ct);

        var bridge = Assert.Single(networks, n => n.Name == "bridge");
        Assert.True(bridge.IsBuiltIn);
        Assert.Equal(new DateTimeOffset(2026, 10, 3, 3, 37, 32, TimeSpan.Zero).AddTicks(9933741), bridge.CreatedAt);
        Assert.False(Assert.Single(networks, n => n.Name == "wslcgui-s1-net").IsBuiltIn);
    }

    [Fact]
    public void Container_list_mounts_are_split()
    {
        var summary = CliContainerQueries.ToSummary(new ContainerListDto("id", "web", "img", null, null, "running", "Up", null, "data,/mnt/{guid}"));

        Assert.Equal(["data", "/mnt/{guid}"], summary.Mounts);
    }
}

public class CliImageExtrasTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public void Build_arguments_cover_the_spec()
    {
        var spec = new BuildSpec(@"C:\src\app")
        {
            Tag = "app:dev",
            Dockerfile = @"C:\src\app\Dockerfile.dev",
            Target = "runtime",
            BuildArgs = new Dictionary<string, string> { ["VERSION"] = "1.2" },
            NoCache = true,
            Pull = true,
        };

        Assert.Equal(
            ["image", "build", "--progress", "plain", "--tag", "app:dev", "--file", @"C:\src\app\Dockerfile.dev", "--target", "runtime",
                "--build-arg", "VERSION=1.2", "--no-cache", "--pull", @"C:\src\app"],
            CliImageService.BuildArguments(spec));
    }

    [Fact]
    public async Task Build_reports_output_and_fails_with_last_errors()
    {
        var cli = new FakeCliRunner();
        cli.Streams[@"image build --progress plain C:\ctx"] =
        [
            new(CliOutputKind.StdErr, "#1 [internal] load build definition"),
            new(CliOutputKind.StdErr, "ERROR: failed to solve: process \"/bin/sh -c exit 3\" did not complete successfully"),
            new(CliOutputKind.StdErr, "Error code: E_FAIL"),
            new(CliOutputKind.Exit, string.Empty, 1),
        ];
        var lines = new List<string>();

        var ex = await Assert.ThrowsAsync<EngineException>(() =>
            new CliImageService(cli).BuildAsync(new BuildSpec(@"C:\ctx"), new SyncProgress(lines.Add), Ct));

        Assert.Equal(3, lines.Count);
        Assert.Contains("failed to solve", ex.Message, StringComparison.Ordinal);
        Assert.Equal("E_FAIL", ex.Code);
    }

    private sealed class SyncProgress(Action<string> report) : IProgress<string>
    {
        public void Report(string value) => report(value);
    }
}

public class CliRegistryAndMaintenanceTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Login_sends_password_on_stdin_not_the_command_line()
    {
        var cli = new FakeCliRunner().Returns("registry login --username me --password-stdin ghcr.io", "Login Succeeded");

        await new CliRegistryService(cli).LoginAsync("ghcr.io", "me", "s3cret", Ct);

        Assert.Equal("s3cret", cli.LastInput);
        Assert.DoesNotContain("s3cret", Assert.Single(cli.Invocations));
    }

    [Fact]
    public async Task Details_flatten_info_json()
    {
        var cli = new FakeCliRunner { ExecutablePath = @"C:\wslc.exe" }.Returns("info --format json", Fixtures.Read("info.json"));

        var details = await new CliEngineMaintenance(cli).GetDetailsAsync(Ct);

        Assert.Equal(new KeyValuePair<string, string>("wslc", @"C:\wslc.exe"), details[0]);
        Assert.Contains(new KeyValuePair<string, string>("Client.Version", "3.0.1.0"), details);
        Assert.Contains(new KeyValuePair<string, string>("Server.Sessions[0].Name", "wslc-cli-will"), details);
    }

    [Fact]
    public void Settings_open_through_wslc_without_a_console()
    {
        var cli = new FakeCliRunner { ExecutablePath = @"C:\wslc.exe" };

        new CliEngineMaintenance(cli).OpenSettingsFile();

        var (executable, arguments) = Assert.Single(cli.Launched);
        Assert.Equal(@"C:\wslc.exe", executable);
        Assert.Equal(["settings"], arguments);
    }
}
