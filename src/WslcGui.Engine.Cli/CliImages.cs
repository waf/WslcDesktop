using System.Globalization;

namespace WslcGui.Engine.Cli;

internal sealed class CliImageService(ICliRunner cli) : IImageService
{
    public async Task<IReadOnlyList<ImageSummary>> ListAsync(CancellationToken cancellationToken = default)
    {
        var result = await cli.RunCheckedAsync(["image", "list", "--no-trunc", "--format", "json"], cancellationToken).ConfigureAwait(false);
        return CliFormats.ParseJsonLines(result.StdOut, CliJsonContext.Default.ImageListDto)
            .Where(dto => !string.IsNullOrEmpty(dto.ID))
            .Select(ToSummary)
            .ToList();
    }

    public async Task<string> InspectAsync(string image, CancellationToken cancellationToken = default)
    {
        var result = await cli.RunCheckedAsync(["image", "inspect", image], cancellationToken).ConfigureAwait(false);
        return result.StdOut;
    }

    /// <remarks>
    /// wslc doesn't flush pull progress when stdout is redirected (S1 §2.10), so no progress is reported yet.
    /// TODO(M2+): run under ConPTY and parse the progress lines.
    /// </remarks>
    public Task PullAsync(string reference, IProgress<PullProgress>? progress = null, CancellationToken cancellationToken = default) =>
        cli.RunCheckedAsync(["image", "pull", reference], cancellationToken);

    public Task RemoveAsync(string image, bool force, CancellationToken cancellationToken = default) =>
        cli.RunCheckedAsync(force ? ["image", "remove", "--force", image] : ["image", "remove", image], cancellationToken);

    public Task TagAsync(string source, string target, CancellationToken cancellationToken = default) =>
        cli.RunCheckedAsync(["image", "tag", source, target], cancellationToken);

    public Task PruneAsync(bool all, CancellationToken cancellationToken = default) =>
        cli.RunCheckedAsync(all ? ["image", "prune", "--force", "--all"] : ["image", "prune", "--force"], cancellationToken);

    public Task BuildAsync(BuildSpec spec, IProgress<string>? output = null, CancellationToken cancellationToken = default) =>
        StreamToProgressAsync(BuildArguments(spec), output, cancellationToken);

    public Task SaveAsync(IReadOnlyList<string> images, string tarPath, CancellationToken cancellationToken = default) =>
        cli.RunCheckedAsync(["image", "save", "--output", tarPath, .. images], cancellationToken);

    public Task LoadAsync(string tarPath, CancellationToken cancellationToken = default) =>
        cli.RunCheckedAsync(["image", "load", "--input", tarPath], cancellationToken);

    public Task ImportAsync(string tarPath, string? reference, CancellationToken cancellationToken = default) =>
        cli.RunCheckedAsync(reference is { Length: > 0 } ? ["image", "import", tarPath, reference] : ["image", "import", tarPath], cancellationToken);

    public Task PushAsync(string reference, IProgress<string>? output = null, CancellationToken cancellationToken = default) =>
        StreamToProgressAsync(["image", "push", reference], output, cancellationToken);

    internal static List<string> BuildArguments(BuildSpec spec)
    {
        // Plain progress gives one line per step, which reads well in a log view (build progress is on stderr, S1 §2.12).
        List<string> arguments = ["image", "build", "--progress", "plain"];
        if (spec.Tag is { Length: > 0 } tag)
        {
            arguments.AddRange(["--tag", tag]);
        }

        if (spec.Dockerfile is { Length: > 0 } dockerfile)
        {
            arguments.AddRange(["--file", dockerfile]);
        }

        if (spec.Target is { Length: > 0 } target)
        {
            arguments.AddRange(["--target", target]);
        }

        foreach (var (key, value) in spec.BuildArgs)
        {
            arguments.AddRange(["--build-arg", $"{key}={value}"]);
        }

        if (spec.NoCache)
        {
            arguments.Add("--no-cache");
        }

        if (spec.Pull)
        {
            arguments.Add("--pull");
        }

        arguments.Add(spec.ContextDirectory);
        return arguments;
    }

    /// <summary>Runs a long command, reporting every output line, and fails with the last error lines if it fails.</summary>
    private async Task StreamToProgressAsync(IReadOnlyList<string> arguments, IProgress<string>? output, CancellationToken cancellationToken)
    {
        var tail = new Queue<string>();
        await foreach (var line in cli.StreamOutputAsync(arguments, cancellationToken).ConfigureAwait(false))
        {
            if (line.Kind == CliOutputKind.Exit)
            {
                if (line.ExitCode != 0)
                {
                    throw CliErrors.FromResult(new CliResult(arguments, line.ExitCode, string.Empty, string.Join('\n', tail), TimeSpan.Zero));
                }

                return;
            }

            output?.Report(line.Text);
            if (line.Kind == CliOutputKind.StdErr)
            {
                tail.Enqueue(line.Text);
                if (tail.Count > 20)
                {
                    tail.Dequeue();
                }
            }
        }
    }

    internal static ImageSummary ToSummary(ImageListDto dto)
    {
        var repository = CliFormats.NoneToNull(dto.Repository);
        return new ImageSummary(
            Id: dto.ID!,
            Repository: repository,
            Tag: repository is null ? null : CliFormats.NoneToNull(dto.Tag),
            SizeBytes: CliFormats.ParseSize(dto.Size) ?? 0,
            CreatedAt: CliFormats.ParseListTimestamp(dto.CreatedAt),
            ContainerCount: int.TryParse(dto.Containers, CultureInfo.InvariantCulture, out var count) ? count : null);
    }
}
