namespace WslcGui.Engine.Cli.Tests;

public class CliEngineInfoTests
{
    [Fact]
    public async Task GetVersion_parses_client_version()
    {
        var cli = new FakeCliRunner().Returns("version --format json", """{"Client":{"Version":"3.0.1.0"}}""");

        var version = await new CliEngineInfo(cli).GetVersionAsync(TestContext.Current.CancellationToken);

        Assert.Equal("3.0.1.0", version.ClientVersion);
    }

    [Fact]
    public async Task CheckHealth_reports_error_with_stderr_message()
    {
        var cli = new FakeCliRunner().Returns("version --format json", "", exitCode: 1, stderr: "The WSL service is not available.\r\n");

        var health = await new CliEngineInfo(cli).CheckHealthAsync(TestContext.Current.CancellationToken);

        Assert.Equal(EngineHealthStatus.Error, health.Status);
        Assert.Equal("The WSL service is not available.", health.Message);
    }

    [Fact]
    public async Task Unexpected_output_raises_engine_exception_with_raw_output()
    {
        var cli = new FakeCliRunner().Returns("version --format json", "not json");

        var ex = await Assert.ThrowsAsync<EngineException>(() => new CliEngineInfo(cli).GetVersionAsync(TestContext.Current.CancellationToken));

        Assert.Equal("not json", ex.Detail);
    }
}
