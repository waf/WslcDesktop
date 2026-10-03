using WslcGui.Engine;

namespace WslcGui.Core.Tests;

public class StatsMonitorTests
{
    private readonly FakeStats _stats = new();
    private readonly FakeEngine _engine = new();
    private bool _running = true;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private StatsMonitor CreateMonitor() => new(_stats, _engine, () => _running);

    [Fact]
    public async Task Samples_and_keeps_bounded_history()
    {
        using var monitor = CreateMonitor();
        var updates = 0;
        monitor.Updated += () => updates++;

        for (var i = 0; i < StatsMonitor.HistoryLength + 5; i++)
        {
            _stats.Next = [Stats("a", cpu: i)];
            await monitor.SampleAsync(Ct);
        }

        Assert.Equal(StatsMonitor.HistoryLength + 5, updates);
        Assert.Equal(StatsMonitor.HistoryLength, monitor.History("a").Count);
        Assert.Equal(StatsMonitor.HistoryLength + 4, monitor.Latest["a"].CpuPercent);
    }

    [Fact]
    public async Task Never_calls_the_engine_when_nothing_runs_or_vm_is_idle()
    {
        using var monitor = CreateMonitor();

        _running = false;
        await monitor.SampleAsync(Ct);
        _running = true;
        _engine.RuntimeState = EngineRuntimeState.Idle;
        await monitor.SampleAsync(Ct);

        Assert.Equal(0, _stats.Calls);
    }

    [Fact]
    public async Task Clears_latest_when_containers_stop()
    {
        using var monitor = CreateMonitor();
        _stats.Next = [Stats("a")];
        await monitor.SampleAsync(Ct);

        _running = false;
        await monitor.SampleAsync(Ct);

        Assert.Empty(monitor.Latest);
    }

    [Fact]
    public async Task Rows_show_cpu_and_memory_of_running_containers()
    {
        _engine.Containers.Add(FakeEngine.Container("a", "web", ContainerState.Running));
        _engine.Containers.Add(FakeEngine.Container("b", "old", ContainerState.Exited));
        using var monitor = CreateMonitor();
        _stats.Next = [Stats("a", cpu: 12.34, memory: 64_000_000)];
        await monitor.SampleAsync(Ct);
        using var list = new ContainersViewModel(_engine, _engine, _engine, new FakeUi(), latestStats: () => monitor.Latest);

        await list.RefreshAsync(cancellationToken: Ct);

        var web = list.Items.Single(r => r.Name == "web");
        Assert.Equal(("12.3%", "64 MB"), (web.CpuText, web.MemoryText));
        Assert.Equal(string.Empty, list.Items.Single(r => r.Name == "old").CpuText);

        _stats.Next = [Stats("a", cpu: 50)];
        await monitor.SampleAsync(Ct);
        list.ApplyStats(monitor.Latest);
        Assert.Equal("50.0%", list.Items.Single(r => r.Name == "web").CpuText);
    }

    private static ContainerStats Stats(string id, double cpu = 1, long memory = 1000) =>
        new(id, id, cpu, memory, 16_000_000_000, 0, 0, 0, 0, 1);

    private sealed class FakeStats : IStatsSource
    {
        public IReadOnlyList<ContainerStats> Next { get; set; } = [];
        public int Calls { get; private set; }

        public Task<IReadOnlyList<ContainerStats>> SnapshotAsync(IReadOnlyList<string> containerIds, CancellationToken cancellationToken = default)
        {
            Calls++;
            return Task.FromResult(Next);
        }
    }
}

public class FilesViewModelTests
{
    private readonly FakeFiles _files = new();
    private readonly FakeUi _ui = new();
    private bool _running = true;

    private FilesViewModel CreateViewModel() => new(_files, "web", () => _running, _ui);

    private static ContainerFileEntry Dir(string path) => new(path[(path.LastIndexOf('/') + 1)..], path, ContainerFileKind.Directory, 0, null, "drwxr-xr-x", null);

    private static ContainerFileEntry File(string path, long size = 10) => new(path[(path.LastIndexOf('/') + 1)..], path, ContainerFileKind.File, size, null, "-rw-r--r--", null);

    private static ContainerFileEntry Link(string path, string target) => new(path[(path.LastIndexOf('/') + 1)..], path, ContainerFileKind.Symlink, 0, null, "lrwxrwxrwx", target);

    [Fact]
    public async Task Lists_live_folders_first()
    {
        _files.Live["/"] = [File("/zfile"), Dir("/etc"), Dir("/bin")];
        var vm = CreateViewModel();

        await vm.NavigateAsync("/");

        Assert.Equal(["bin", "etc", "zfile"], vm.Entries.Select(e => e.Name));
        Assert.False(vm.IsSnapshot);
        Assert.Null(vm.ModeText);
    }

    [Fact]
    public async Task Stopped_container_uses_a_snapshot_once()
    {
        _running = false;
        _files.Snapshot["/"] = [Dir("/etc")];
        _files.Snapshot["/etc"] = [File("/etc/hostname")];
        var vm = CreateViewModel();

        await vm.NavigateAsync("/");
        await vm.OpenAsync(vm.Entries[0]);

        Assert.Equal("/etc", vm.CurrentPath);
        Assert.Equal(["hostname"], vm.Entries.Select(e => e.Name));
        Assert.True(vm.IsSnapshot);
        Assert.Contains("stopped", vm.ModeText, StringComparison.Ordinal);
        Assert.Equal(1, _files.SnapshotsTaken);
        Assert.False(vm.CanUpload);
    }

    [Fact]
    public async Task Image_without_tools_falls_back_to_snapshot()
    {
        _files.ListError = new EngineException(EngineErrorKind.NotSupported, "no find");
        _files.Snapshot["/"] = [Dir("/app")];
        var vm = CreateViewModel();

        await vm.NavigateAsync("/");

        Assert.Equal(["app"], vm.Entries.Select(e => e.Name));
        Assert.Contains("no tools", vm.ModeText, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Symlink_to_folder_navigates_and_to_file_previews()
    {
        _files.Live["/"] = [Link("/bin", "usr/bin"), Link("/motd", "/etc/motd")];
        _files.Live["/usr/bin"] = [File("/usr/bin/sh")];
        _files.Content["/motd"] = "welcome"u8.ToArray();
        var vm = CreateViewModel();
        await vm.NavigateAsync("/");
        var motd = vm.Entries.Single(e => e.Name == "motd");

        await vm.OpenAsync(vm.Entries.Single(e => e.Name == "bin"));
        Assert.Equal("/usr/bin", vm.CurrentPath);

        await vm.OpenAsync(motd);
        Assert.Equal("welcome", vm.PreviewText);
        Assert.Null(vm.Error);
    }

    [Fact]
    public async Task Up_and_path_normalization()
    {
        _files.Live["/"] = [];
        _files.Live["/usr"] = [];
        var vm = CreateViewModel();

        await vm.NavigateAsync(@"/usr//share/../");
        Assert.Equal("/usr", vm.CurrentPath);

        await vm.UpAsync();
        Assert.Equal("/", vm.CurrentPath);
    }

    [Fact]
    public async Task Missing_folder_shows_error_and_keeps_listing()
    {
        _files.Live["/"] = [Dir("/etc")];
        var vm = CreateViewModel();
        await vm.NavigateAsync("/");

        await vm.NavigateAsync("/nope");

        Assert.Equal("/", vm.CurrentPath);
        Assert.Equal("missing /nope", vm.Error);
    }

    [Theory]
    [InlineData(new byte[] { 104, 105 }, 2, "hi")]
    [InlineData(new byte[] { 1, 0, 2 }, 3, "Binary file (3 B). Download it to open it.")]
    public void Describes_preview_content(byte[] bytes, long size, string expected) =>
        Assert.Equal(expected, FilesViewModel.DescribeContent(bytes, size));

    [Fact]
    public async Task Large_files_are_not_previewed()
    {
        var vm = CreateViewModel();

        await vm.PreviewAsync(new FileRow(File("/big", FilesViewModel.MaxPreviewFileBytes + 1)));

        Assert.Contains("too large", vm.PreviewText, StringComparison.Ordinal);
        Assert.Empty(_files.Reads);
    }

    [Fact]
    public async Task Upload_goes_to_current_folder_and_reloads()
    {
        _files.Live["/tmp"] = [];
        var vm = CreateViewModel();
        await vm.NavigateAsync("/tmp");

        await vm.UploadAsync([@"C:\a.txt"]);

        Assert.Equal([@"C:\a.txt -> /tmp"], _files.Uploads);
        Assert.Single(_ui.Toasts);
    }

    private sealed class FakeFiles : IContainerFiles
    {
        public Dictionary<string, IReadOnlyList<ContainerFileEntry>> Live { get; } = [];
        public Dictionary<string, IReadOnlyList<ContainerFileEntry>> Snapshot { get; } = [];
        public Dictionary<string, byte[]> Content { get; } = [];
        public EngineException? ListError { get; set; }
        public int SnapshotsTaken { get; private set; }
        public List<string> Uploads { get; } = [];
        public List<string> Reads { get; } = [];

        public Task<IReadOnlyList<ContainerFileEntry>> ListDirectoryAsync(string containerId, string path, CancellationToken cancellationToken = default)
        {
            if (ListError is { } error)
            {
                return Task.FromException<IReadOnlyList<ContainerFileEntry>>(error);
            }

            return Live.TryGetValue(path, out var entries)
                ? Task.FromResult(entries)
                : Task.FromException<IReadOnlyList<ContainerFileEntry>>(new EngineException(
                    Content.ContainsKey(path) ? EngineErrorKind.InvalidArgument : EngineErrorKind.NotFound, $"missing {path}"));
        }

        public Task<IContainerFileSnapshot> OpenSnapshotAsync(string containerId, CancellationToken cancellationToken = default)
        {
            SnapshotsTaken++;
            return Task.FromResult<IContainerFileSnapshot>(new Snap(Snapshot));
        }

        public Task DownloadAsync(string containerId, string containerPath, string localDirectory, CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task UploadAsync(string containerId, string localPath, string containerDirectory, CancellationToken cancellationToken = default)
        {
            Uploads.Add($"{localPath} -> {containerDirectory}");
            return Task.CompletedTask;
        }

        public Task<byte[]> ReadFileStartAsync(string containerId, string containerPath, int maxBytes, CancellationToken cancellationToken = default)
        {
            Reads.Add(containerPath);
            return Task.FromResult(Content[containerPath]);
        }

        private sealed class Snap(Dictionary<string, IReadOnlyList<ContainerFileEntry>> entries) : IContainerFileSnapshot
        {
            public IReadOnlyList<ContainerFileEntry>? List(string path) => entries.GetValueOrDefault(path);
        }
    }
}
