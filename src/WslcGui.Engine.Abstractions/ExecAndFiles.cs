namespace WslcGui.Engine;

public interface IExecService
{
    /// <summary>Runs a non-interactive command in a running container and collects its output.</summary>
    Task<ExecResult> ExecAsync(string containerId, IReadOnlyList<string> command, CancellationToken cancellationToken = default);
}

public sealed record ExecResult(int ExitCode, string StdOut, string StdErr);

public interface IContainerFiles
{
    /// <summary>
    /// Lists a directory of a running container. Throws <see cref="EngineException"/> with
    /// <see cref="EngineErrorKind.NotRunning"/> for a stopped container and <see cref="EngineErrorKind.NotSupported"/>
    /// when the image lacks the tools to list files; use <see cref="OpenSnapshotAsync"/> then.
    /// </summary>
    Task<IReadOnlyList<ContainerFileEntry>> ListDirectoryAsync(string containerId, string path, CancellationToken cancellationToken = default);

    /// <summary>Reads the whole filesystem listing at once (works for stopped containers; slower, no volume contents).</summary>
    Task<IContainerFileSnapshot> OpenSnapshotAsync(string containerId, CancellationToken cancellationToken = default);

    /// <summary>Copies a file or directory (following a symlink to its target) out of the container into <paramref name="localDirectory"/>.</summary>
    Task DownloadAsync(string containerId, string containerPath, string localDirectory, CancellationToken cancellationToken = default);

    /// <summary>Copies a local file or directory into the existing container directory <paramref name="containerDirectory"/>, keeping its name.</summary>
    Task UploadAsync(string containerId, string localPath, string containerDirectory, CancellationToken cancellationToken = default);

    /// <summary>Reads up to <paramref name="maxBytes"/> from the start of a file (following symlinks).</summary>
    Task<byte[]> ReadFileStartAsync(string containerId, string containerPath, int maxBytes, CancellationToken cancellationToken = default);
}

/// <summary>A point-in-time listing of a container's whole filesystem.</summary>
public interface IContainerFileSnapshot
{
    /// <summary>Entries directly inside <paramref name="path"/>, or null if the directory doesn't exist in the snapshot.</summary>
    IReadOnlyList<ContainerFileEntry>? List(string path);
}

public enum ContainerFileKind
{
    File,
    Directory,
    Symlink,
    Other,
}

/// <param name="Path">Absolute path in the container.</param>
/// <param name="Permissions">As <c>ls -l</c> shows them, for example <c>drwxr-xr-x</c>.</param>
public sealed record ContainerFileEntry(
    string Name,
    string Path,
    ContainerFileKind Kind,
    long SizeBytes,
    DateTimeOffset? Modified,
    string Permissions,
    string? LinkTarget);
