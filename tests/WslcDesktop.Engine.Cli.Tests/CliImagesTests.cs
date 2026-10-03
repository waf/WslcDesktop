namespace WslcDesktop.Engine.Cli.Tests;

public class CliImagesTests
{
    private const string List = "image list --no-trunc --format json";

    [Fact]
    public async Task List_maps_captured_output()
    {
        var cli = new FakeCliRunner().Returns(List, Fixtures.Read("image-list.no-trunc.json"));

        var images = await new CliImageService(cli).ListAsync(TestContext.Current.CancellationToken);

        var debian = Assert.Single(images, i => i.Repository == "debian");
        Assert.Equal("bookworm-slim", debian.Tag);
        Assert.Equal("debian:bookworm-slim", debian.Reference);
        Assert.StartsWith("sha256:", debian.Id, StringComparison.Ordinal);
        Assert.Equal(74_800_000L, debian.SizeBytes);
        Assert.Equal(1, debian.ContainerCount);
        Assert.NotNull(debian.CreatedAt);
    }

    [Fact]
    public void Untagged_image_is_referenced_by_id()
    {
        var image = CliImageService.ToSummary(new ImageListDto("sha256:abc", "<none>", "<none>", "1MB", null, "0"));

        Assert.Null(image.Repository);
        Assert.Null(image.Tag);
        Assert.Equal("sha256:abc", image.Reference);
    }
}
