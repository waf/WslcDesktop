namespace WslcGui.Engine;

public interface IExecService
{
    /// <summary>Runs a non-interactive command in a running container and collects its output.</summary>
    Task<ExecResult> ExecAsync(string containerId, IReadOnlyList<string> command, CancellationToken cancellationToken = default);
}

public sealed record ExecResult(int ExitCode, string StdOut, string StdErr);

public interface IContainerFiles
{
    Task<IReadOnlyList<ContainerFileEntry>> ListDirectoryAsync(string containerId, string path, CancellationToken cancellationToken = default);

    /// <summary>Copies a file or directory out of the container to a local path.</summary>
    Task CopyFromAsync(string containerId, string containerPath, string localPath, CancellationToken cancellationToken = default);

    /// <summary>Copies a local file or directory into the container.</summary>
    Task CopyToAsync(string containerId, string localPath, string containerPath, CancellationToken cancellationToken = default);

    /// <summary>Exports the container filesystem as a tar archive.</summary>
    Task ExportAsync(string containerId, string tarPath, CancellationToken cancellationToken = default);
}

public enum ContainerFileKind
{
    File,
    Directory,
    Symlink,
    Other,
}

public sealed record ContainerFileEntry(
    string Name,
    ContainerFileKind Kind,
    long SizeBytes,
    DateTimeOffset? Modified,
    string Permissions,
    string? LinkTarget);
