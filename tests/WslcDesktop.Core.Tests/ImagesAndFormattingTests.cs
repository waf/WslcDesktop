using System.Collections.ObjectModel;

using WslcDesktop.Engine;

namespace WslcDesktop.Core.Tests;

public class ImagesViewModelTests
{
    private readonly FakeEngine _engine = new();
    private readonly FakeUi _ui = new();

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Untagged_images_sort_last_and_removal_forces_in_use_images()
    {
        _engine.Images.Add(new ImageSummary("sha256:1", null, null, 10, null, 0));
        _engine.Images.Add(new ImageSummary("sha256:2", "redis", "7-alpine", 39_100_000, null, 1));
        _engine.Images.Add(new ImageSummary("sha256:3", "alpine", "latest", 8_420_000, null, 0));
        var vm = new ImagesViewModel(_engine, _engine, _ui);
        await vm.RefreshAsync(cancellationToken: Ct);

        Assert.Equal(["alpine:latest", "redis:7-alpine", "sha256:1"], vm.Items.Select(i => i.Reference));
        Assert.Equal("39.1 MB", vm.Items[1].SizeText);

        await vm.RemoveAsync([vm.Items[0], vm.Items[1]]);

        Assert.Equal(["rmi alpine:latest force=False", "rmi redis:7-alpine force=True"], _engine.Calls.Order());
    }
}

public class FormattingTests
{
    [Theory]
    [InlineData(0L, "0 B")]
    [InlineData(999L, "999 B")]
    [InlineData(8_420_000L, "8.4 MB")]
    [InlineData(294_000_000L, "294 MB")]
    [InlineData(1_500_000_000L, "1.5 GB")]
    public void Bytes_uses_decimal_units(long bytes, string expected) => Assert.Equal(expected, Formatting.Bytes(bytes));

    [Fact]
    public void Ago_is_relative()
    {
        var now = new DateTimeOffset(2026, 10, 3, 12, 0, 0, TimeSpan.Zero);

        Assert.Equal("just now", Formatting.Ago(now.AddSeconds(-5), now));
        Assert.Equal("1 minute ago", Formatting.Ago(now.AddMinutes(-1), now));
        Assert.Equal("3 hours ago", Formatting.Ago(now.AddHours(-3), now));
        Assert.Equal("2 days ago", Formatting.Ago(now.AddDays(-2), now));
        Assert.Equal(string.Empty, Formatting.Ago(null, now));
    }

    [Fact]
    public void ShortId_strips_digest_prefix() =>
        Assert.Equal("db9f02c6bde9", Formatting.ShortId("sha256:db9f02c6bde9fa90cc8074c92754b2b046947392f1a857726e77f041febb7b82"));
}

public class CollectionSyncTests
{
    [Theory]
    [InlineData("abc", "abc")]
    [InlineData("abc", "cab")]
    [InlineData("abc", "xbz")]
    [InlineData("", "abc")]
    [InlineData("abcd", "")]
    [InlineData("abcdef", "fbdx")]
    public void Sync_produces_source_order(string initial, string wanted)
    {
        var target = new ObservableCollection<string>(initial.Select(c => c.ToString()));

        CollectionSync.Sync(target, wanted.Select(c => c.ToString()).ToList(), s => s);

        Assert.Equal(wanted.Select(c => c.ToString()), target);
    }

    [Fact]
    public void Sync_replaces_changed_values_with_the_same_key()
    {
        var target = new ObservableCollection<Pair>([new("a", 1), new("b", 1)]);
        var changes = 0;
        target.CollectionChanged += (_, _) => changes++;

        CollectionSync.Sync(target, [new("a", 1), new("b", 2)], x => x.Key);

        Assert.Equal([new("a", 1), new("b", 2)], target);
        Assert.Equal(1, changes);
    }

    private sealed record Pair(string Key, int Value);
}
