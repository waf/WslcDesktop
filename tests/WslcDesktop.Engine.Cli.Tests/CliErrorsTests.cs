namespace WslcDesktop.Engine.Cli.Tests;

public class CliErrorsTests
{
    [Theory]
    [InlineData("error-bad-option.txt", EngineErrorKind.InvalidArgument, null)]
    [InlineData("error-container-inspect-not-found.txt", EngineErrorKind.NotFound, null)]
    [InlineData("error-container-logs-not-found.txt", EngineErrorKind.NotFound, "WSLC_E_CONTAINER_NOT_FOUND")]
    [InlineData("error-container-remove-running.txt", EngineErrorKind.Conflict, "WSLC_E_CONTAINER_IS_RUNNING")]
    [InlineData("error-container-start-not-found.txt", EngineErrorKind.NotFound, "WSLC_E_CONTAINER_NOT_FOUND")]
    [InlineData("error-exec-stopped.txt", EngineErrorKind.NotRunning, "WSLC_E_CONTAINER_NOT_RUNNING")]
    [InlineData("error-image-inspect-not-found.txt", EngineErrorKind.NotFound, null)]
    [InlineData("error-image-pull-not-found.txt", EngineErrorKind.NotFound, "WSLC_E_IMAGE_NOT_FOUND")]
    [InlineData("error-image-remove-not-found.txt", EngineErrorKind.NotFound, "WSLC_E_IMAGE_NOT_FOUND")]
    [InlineData("error-inspect-generic-not-found.txt", EngineErrorKind.NotFound, null)]
    [InlineData("error-network-inspect-not-found.txt", EngineErrorKind.NotFound, null)]
    [InlineData("error-run-name-conflict.txt", EngineErrorKind.Conflict, "ERROR_ALREADY_EXISTS")]
    [InlineData("error-session-not-found.txt", EngineErrorKind.Unavailable, "WSLC_E_SESSION_NOT_FOUND")]
    [InlineData("error-stats-not-found.txt", EngineErrorKind.NotFound, "WSLC_E_CONTAINER_NOT_FOUND")]
    [InlineData("error-volume-inspect-not-found.txt", EngineErrorKind.NotFound, null)]
    public void Maps_captured_errors(string fixture, EngineErrorKind expectedKind, string? expectedCode)
    {
        var ex = CliErrors.FromResult(Fixtures.ReadResult(fixture));

        Assert.Equal(expectedKind, ex.Kind);
        Assert.Equal(expectedCode, ex.Code);
        Assert.DoesNotContain("Error code:", ex.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("If this error was unexpected", ex.Message, StringComparison.Ordinal);
        Assert.False(string.IsNullOrWhiteSpace(ex.Message));
    }

    [Fact]
    public void Message_is_the_human_readable_line()
    {
        var ex = CliErrors.FromResult(Fixtures.ReadResult("error-container-start-not-found.txt"));

        Assert.Equal("Container 'WslcDesktop-s1-doesnotexist' not found.", ex.Message);
    }
}
