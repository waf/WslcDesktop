namespace WslcDesktop.Engine.Cli.Tests;

public class CliFormatsTests
{
    [Fact]
    public void Parses_list_timestamp_with_offset()
    {
        var value = CliFormats.ParseListTimestamp("2026-10-03 10:37:36 +0700 GMT+7");

        Assert.Equal(new DateTimeOffset(2026, 10, 3, 10, 37, 36, TimeSpan.FromHours(7)), value);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("2 weeks ago")]
    public void Unparseable_timestamp_is_null(string? value) => Assert.Null(CliFormats.ParseListTimestamp(value));

    [Fact]
    public void Parses_published_ports()
    {
        var ports = CliFormats.ParsePorts("127.0.0.1:15432->5432/tcp, [::1]:8080->80/tcp, 53/udp");

        Assert.Equal(
            [
                new PortMapping(5432, 15432, "127.0.0.1", PortProtocol.Tcp),
                new PortMapping(80, 8080, "::1", PortProtocol.Tcp),
                new PortMapping(53, null, null, PortProtocol.Udp),
            ],
            ports);
    }

    [Theory]
    [InlineData("0B", 0L)]
    [InlineData("74.8MB", 74_800_000L)]
    [InlineData("1.25kB", 1_250L)]
    [InlineData("63B (virtual 294MB)", 63L)]
    [InlineData("107.5MiB", 112_721_920L)]
    [InlineData("15.31GiB", 16_438_987_325L)]
    public void Parses_sizes(string text, long expected) => Assert.Equal(expected, CliFormats.ParseSize(text));

    [Theory]
    [InlineData(null)]
    [InlineData("N/A")]
    [InlineData("12parsecs")]
    public void Unparseable_size_is_null(string? text) => Assert.Null(CliFormats.ParseSize(text));

    [Fact]
    public void Empty_json_lines_output_is_an_empty_list() =>
        Assert.Empty(CliFormats.ParseJsonLines(Fixtures.Read("container-list-a.empty.json"), CliJsonContext.Default.ContainerListDto));
}
