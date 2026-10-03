using System.Formats.Tar;

namespace WslcDesktop.Engine.Cli.Tests;

public class CliFilesTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    // Captured in spike S4 (docs/spikes/S4-files-and-stats.md).
    private const string BusyBoxOutput = """
        directory|4096|1791004864|drwxr-xr-x|/tmp/t/sub dir
        regular empty file|0|1791004864|-rw-r--r--|/tmp/t/pipe|name
        symbolic link|13|1791004864|lrwxrwxrwx|'/tmp/t/link' -> '/etc/hostname'
        regular file|100|1791004864|-rw-r--r--|/tmp/t/data
        """;

    private const string GnuOutput = """
        directory|4096|1791004864|drwxr-xr-x|/tmp/t/sub dir
        regular empty file|0|1791004864|-rw-r--r--|/tmp/t/pipe|name
        symbolic link|13|1791004864|lrwxrwxrwx|/tmp/t/link -> /etc/hostname
        regular file|100|1791004864|-rw-r--r--|/tmp/t/data
        """;

    [Theory]
    [InlineData(BusyBoxOutput)]
    [InlineData(GnuOutput)]
    public void Parses_stat_listing_from_busybox_and_gnu(string output)
    {
        var entries = CliContainerFiles.ParseStatOutput(output);

        Assert.Equal(
            [
                new ContainerFileEntry("sub dir", "/tmp/t/sub dir", ContainerFileKind.Directory, 4096, DateTimeOffset.FromUnixTimeSeconds(1791004864), "drwxr-xr-x", null),
                new ContainerFileEntry("pipe|name", "/tmp/t/pipe|name", ContainerFileKind.File, 0, DateTimeOffset.FromUnixTimeSeconds(1791004864), "-rw-r--r--", null),
                new ContainerFileEntry("link", "/tmp/t/link", ContainerFileKind.Symlink, 13, DateTimeOffset.FromUnixTimeSeconds(1791004864), "lrwxrwxrwx", "/etc/hostname"),
                new ContainerFileEntry("data", "/tmp/t/data", ContainerFileKind.File, 100, DateTimeOffset.FromUnixTimeSeconds(1791004864), "-rw-r--r--", null),
            ],
            entries);
    }

    [Fact]
    public async Task Lists_with_find_and_stat()
    {
        var cli = new FakeCliRunner().Returns(
            "container exec --env QUOTING_STYLE=literal web find /tmp/t -mindepth 1 -maxdepth 1 -exec stat -c %F|%s|%Y|%A|%N {} +",
            GnuOutput);

        var entries = await new CliContainerFiles(cli).ListDirectoryAsync("web", "/tmp/t", Ct);

        Assert.Equal(4, entries.Count);
    }

    [Theory]
    [InlineData(1, "find: '/nope': No such file or directory", EngineErrorKind.NotFound)]
    [InlineData(1, "find: '/etc/hostname': Not a directory", EngineErrorKind.InvalidArgument)]
    [InlineData(126, "", EngineErrorKind.NotSupported)]
    [InlineData(1, "Container 'web' is not running.\nError code: WSLC_E_CONTAINER_NOT_RUNNING", EngineErrorKind.NotRunning)]
    public async Task Maps_listing_failures(int exitCode, string stderr, EngineErrorKind expected)
    {
        var cli = new FakeCliRunner().Returns(
            "container exec --env QUOTING_STYLE=literal web find /x -mindepth 1 -maxdepth 1 -exec stat -c %F|%s|%Y|%A|%N {} +",
            "", exitCode, stderr);

        var ex = await Assert.ThrowsAsync<EngineException>(() => new CliContainerFiles(cli).ListDirectoryAsync("web", "/x", Ct));

        Assert.Equal(expected, ex.Kind);
    }

    [Fact]
    public async Task Partial_listing_with_permission_errors_is_returned()
    {
        var cli = new FakeCliRunner().Returns(
            "container exec --env QUOTING_STYLE=literal web find /x -mindepth 1 -maxdepth 1 -exec stat -c %F|%s|%Y|%A|%N {} +",
            "regular file|1|0|-rw-------|/x/a\n", 1, "find: '/x/secret': Permission denied");

        var entries = await new CliContainerFiles(cli).ListDirectoryAsync("web", "/x", Ct);

        Assert.Equal("a", Assert.Single(entries).Name);
    }

    [Fact]
    public async Task Download_follows_links_into_a_directory_target()
    {
        var cli = new FakeCliRunner().Returns(@"container cp --follow-link web:/etc/os-release C:\out\", "");

        await new CliContainerFiles(cli).DownloadAsync("web", "/etc/os-release", @"C:\out", Ct);

        Assert.Single(cli.Invocations);
    }

    [Fact]
    public async Task Upload_targets_the_container_directory()
    {
        var cli = new FakeCliRunner().Returns(@"container cp C:\in\a.txt web:/tmp/", "");

        await new CliContainerFiles(cli).UploadAsync("web", @"C:\in\a.txt", "/tmp", Ct);

        Assert.Single(cli.Invocations);
    }

    [Fact]
    public async Task Tar_snapshot_indexes_directories_links_and_implicit_parents()
    {
        using var tar = new MemoryStream();
        await using (var writer = new TarWriter(tar, TarEntryFormat.Pax, leaveOpen: true))
        {
            writer.WriteEntry(new PaxTarEntry(TarEntryType.Directory, "etc/") { Mode = (UnixFileMode)0b111_101_101 });
            writer.WriteEntry(new PaxTarEntry(TarEntryType.RegularFile, "etc/hostname") { DataStream = new MemoryStream("box\n"u8.ToArray()), Mode = (UnixFileMode)0b110_100_100 });
            writer.WriteEntry(new PaxTarEntry(TarEntryType.SymbolicLink, "bin") { LinkName = "usr/bin" });
            writer.WriteEntry(new PaxTarEntry(TarEntryType.RegularFile, "usr/share/doc/readme") { DataStream = new MemoryStream([1, 2, 3]) });
        }

        tar.Position = 0;
        var snapshot = await TarSnapshot.ReadAsync(tar, Ct);

        Assert.Equal(["etc", "bin", "usr"], snapshot.List("/")!.Select(e => e.Name));
        var hostname = Assert.Single(snapshot.List("/etc")!);
        Assert.Equal(("/etc/hostname", 4L, "-rw-r--r--"), (hostname.Path, hostname.SizeBytes, hostname.Permissions));
        Assert.Equal("usr/bin", snapshot.List("/")!.Single(e => e.Name == "bin").LinkTarget);
        Assert.Equal("readme", Assert.Single(snapshot.List("/usr/share/doc/")!).Name);
        Assert.Null(snapshot.List("/nope"));
    }
}

public class CliStatsTests
{
    [Fact]
    public async Task Parses_captured_stats()
    {
        var cli = new FakeCliRunner().Returns("container stats --format json", Fixtures.Read("stats.json"));

        var stats = await new CliStatsSource(cli).SnapshotAsync([], TestContext.Current.CancellationToken);

        var redis = Assert.Single(stats, s => s.Name == "wslcgui-s1-redis");
        Assert.Equal(0.31, redis.CpuPercent, precision: 2);
        Assert.Equal((long)Math.Round(14.16 * (1 << 20)), redis.MemoryUsageBytes);
        Assert.Equal((long)Math.Round(15.31 * (1L << 30)), redis.MemoryLimitBytes);
        Assert.Equal(1250, redis.NetworkRxBytes);
        Assert.Equal(10_600_000, redis.BlockReadBytes);
        Assert.Equal(6, redis.Pids);
    }
}
