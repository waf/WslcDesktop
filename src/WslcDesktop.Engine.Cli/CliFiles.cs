using System.Formats.Tar;
using System.Globalization;
using System.Text;

namespace WslcDesktop.Engine.Cli;

/// <summary>Container filesystem access via exec (find/stat), <c>container cp</c> and <c>container export</c> (see S4).</summary>
internal sealed class CliContainerFiles(ICliRunner cli) : IContainerFiles
{
    // Path is the last field so names containing '|' survive; %N adds " -> target" for symlinks.
    private const string StatFormat = "%F|%s|%Y|%A|%N";

    public async Task<IReadOnlyList<ContainerFileEntry>> ListDirectoryAsync(string containerId, string path, CancellationToken cancellationToken = default)
    {
        // QUOTING_STYLE=literal stops GNU stat quoting names in %N; BusyBox ignores it (and quotes symlinks only).
        var result = await cli.RunAsync(
            ["container", "exec", "--env", "QUOTING_STYLE=literal", containerId, "find", path, "-mindepth", "1", "-maxdepth", "1", "-exec", "stat", "-c", StatFormat, "{}", "+"],
            cancellationToken).ConfigureAwait(false);

        if (result.StdErr.Contains("Error code: ", StringComparison.Ordinal))
        {
            throw CliErrors.FromResult(result);
        }

        var entries = ParseStatOutput(result.StdOut);
        if (result.ExitCode == 0 || entries.Count > 0)
        {
            // A non-zero exit with output means some entries were unreadable (permission denied); show the rest.
            return entries;
        }

        if (result.ExitCode is 126 or 127)
        {
            throw new EngineException(EngineErrorKind.NotSupported, "This container has no shell tools (find/stat) to list files with.")
            {
                Detail = result.StdOut + result.StdErr,
            };
        }

        var message = FirstLine(result.StdErr) ?? $"Listing {path} failed (exit code {result.ExitCode}).";
        var kind = message.Contains("No such file", StringComparison.OrdinalIgnoreCase) ? EngineErrorKind.NotFound
            : message.Contains("Not a directory", StringComparison.OrdinalIgnoreCase) ? EngineErrorKind.InvalidArgument
            : EngineErrorKind.Unknown;
        throw new EngineException(kind, message) { Detail = result.StdErr };
    }

    public async Task<IContainerFileSnapshot> OpenSnapshotAsync(string containerId, CancellationToken cancellationToken = default)
    {
        var directory = CreateTempDirectory();
        try
        {
            var tar = Path.Combine(directory, "export.tar");
            await cli.RunCheckedAsync(["container", "export", "--output", tar, containerId], cancellationToken).ConfigureAwait(false);
            await using var stream = File.OpenRead(tar);
            return await TarSnapshot.ReadAsync(stream, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            DeleteQuietly(directory);
        }
    }

    public Task DownloadAsync(string containerId, string containerPath, string localDirectory, CancellationToken cancellationToken = default) =>
        cli.RunCheckedAsync(["container", "cp", "--follow-link", $"{containerId}:{containerPath}", WithTrailingSeparator(localDirectory)], cancellationToken);

    public Task UploadAsync(string containerId, string localPath, string containerDirectory, CancellationToken cancellationToken = default) =>
        cli.RunCheckedAsync(["container", "cp", localPath, $"{containerId}:{(containerDirectory.EndsWith('/') ? containerDirectory : containerDirectory + "/")}"], cancellationToken);

    public async Task<byte[]> ReadFileStartAsync(string containerId, string containerPath, int maxBytes, CancellationToken cancellationToken = default)
    {
        var directory = CreateTempDirectory();
        try
        {
            await DownloadAsync(containerId, containerPath, directory, cancellationToken).ConfigureAwait(false);
            var file = Directory.EnumerateFiles(directory).FirstOrDefault()
                ?? throw new EngineException(EngineErrorKind.NotSupported, $"{containerPath} is not a regular file.");

            await using var stream = File.OpenRead(file);
            var buffer = new byte[(int)Math.Min(maxBytes, stream.Length)];
            await stream.ReadExactlyAsync(buffer, cancellationToken).ConfigureAwait(false);
            return buffer;
        }
        finally
        {
            DeleteQuietly(directory);
        }
    }

    /// <summary>Parses <c>find -exec stat -c '%F|%s|%Y|%A|%N'</c> output from GNU or BusyBox stat.</summary>
    internal static List<ContainerFileEntry> ParseStatOutput(string output)
    {
        var entries = new List<ContainerFileEntry>();
        foreach (var rawLine in output.Split('\n'))
        {
            var line = rawLine.TrimEnd('\r');
            var fields = line.Split('|', 5);
            if (fields.Length < 5 || !long.TryParse(fields[1], CultureInfo.InvariantCulture, out var size))
            {
                continue;
            }

            var kind = fields[0] switch
            {
                "directory" => ContainerFileKind.Directory,
                "regular file" or "regular empty file" => ContainerFileKind.File,
                "symbolic link" => ContainerFileKind.Symlink,
                _ => ContainerFileKind.Other,
            };

            var path = fields[4];
            string? target = null;
            if (kind == ContainerFileKind.Symlink && path.IndexOf(" -> ", StringComparison.Ordinal) is var arrow and >= 0)
            {
                target = Unquote(path[(arrow + 4)..]);
                path = Unquote(path[..arrow]);
            }

            DateTimeOffset? modified = long.TryParse(fields[2], CultureInfo.InvariantCulture, out var seconds)
                ? DateTimeOffset.FromUnixTimeSeconds(seconds)
                : null;
            entries.Add(new ContainerFileEntry(FileName(path), path, kind, size, modified, fields[3], target));
        }

        return entries;
    }

    private static string Unquote(string text) =>
        text.Length >= 2 && text[0] == '\'' && text[^1] == '\'' ? text[1..^1] : text;

    private static string FileName(string path)
    {
        var trimmed = path.TrimEnd('/');
        var slash = trimmed.LastIndexOf('/');
        return slash >= 0 ? trimmed[(slash + 1)..] : trimmed;
    }

    private static string? FirstLine(string text) =>
        text.Split('\n').Select(l => l.Trim()).FirstOrDefault(l => l.Length > 0);

    private static string WithTrailingSeparator(string directory) =>
        Path.EndsInDirectorySeparator(directory) ? directory : directory + Path.DirectorySeparatorChar;

    private static string CreateTempDirectory() =>
        Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "WslcDesktop", Guid.NewGuid().ToString("N"))).FullName;

    private static void DeleteQuietly(string directory)
    {
        try
        {
            Directory.Delete(directory, recursive: true);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}

/// <summary>An in-memory index of a <c>container export</c> tar.</summary>
internal sealed class TarSnapshot : IContainerFileSnapshot
{
    private readonly Dictionary<string, List<ContainerFileEntry>> _directories = new(StringComparer.Ordinal) { ["/"] = [] };

    public IReadOnlyList<ContainerFileEntry>? List(string path) =>
        _directories.TryGetValue(Normalize(path), out var entries) ? entries : null;

    internal static async Task<TarSnapshot> ReadAsync(Stream tar, CancellationToken cancellationToken)
    {
        var snapshot = new TarSnapshot();
        await using var reader = new TarReader(tar);
        while (await reader.GetNextEntryAsync(copyData: false, cancellationToken).ConfigureAwait(false) is { } entry)
        {
            snapshot.Add(entry);
        }

        return snapshot;
    }

    private void Add(TarEntry entry)
    {
        var path = Normalize("/" + entry.Name);
        if (path == "/")
        {
            return;
        }

        var kind = entry.EntryType switch
        {
            TarEntryType.Directory => ContainerFileKind.Directory,
            TarEntryType.RegularFile or TarEntryType.V7RegularFile or TarEntryType.ContiguousFile or TarEntryType.HardLink => ContainerFileKind.File,
            TarEntryType.SymbolicLink => ContainerFileKind.Symlink,
            _ => ContainerFileKind.Other,
        };

        var parent = Parent(path);
        EnsureDirectory(parent);
        var siblings = _directories[parent];
        var name = path[(path.LastIndexOf('/') + 1)..];
        var file = new ContainerFileEntry(
            name,
            path,
            kind,
            kind == ContainerFileKind.File ? entry.Length : 0,
            entry.ModificationTime,
            Permissions(kind, entry.Mode),
            kind == ContainerFileKind.Symlink ? entry.LinkName : null);

        // An implicit parent may have been added before its own entry; replace it with the real one.
        var existing = siblings.FindIndex(e => e.Name == name);
        if (existing >= 0)
        {
            siblings[existing] = file;
        }
        else
        {
            siblings.Add(file);
        }

        if (kind == ContainerFileKind.Directory)
        {
            _directories.TryAdd(path, []);
        }
    }

    private void EnsureDirectory(string path)
    {
        if (_directories.ContainsKey(path))
        {
            return;
        }

        var parent = Parent(path);
        EnsureDirectory(parent);
        _directories[path] = [];
        var name = path[(path.LastIndexOf('/') + 1)..];
        _directories[parent].Add(new ContainerFileEntry(name, path, ContainerFileKind.Directory, 0, null, "drwxr-xr-x", null));
    }

    private static string Normalize(string path)
    {
        var trimmed = path.Replace("/./", "/", StringComparison.Ordinal).TrimEnd('/');
        while (trimmed.Contains("//", StringComparison.Ordinal))
        {
            trimmed = trimmed.Replace("//", "/", StringComparison.Ordinal);
        }

        return trimmed.Length == 0 ? "/" : trimmed;
    }

    private static string Parent(string path)
    {
        var slash = path.LastIndexOf('/');
        return slash <= 0 ? "/" : path[..slash];
    }

    internal static string Permissions(ContainerFileKind kind, UnixFileMode mode)
    {
        var builder = new StringBuilder(10);
        builder.Append(kind switch { ContainerFileKind.Directory => 'd', ContainerFileKind.Symlink => 'l', _ => '-' });
        builder.Append(mode.HasFlag(UnixFileMode.UserRead) ? 'r' : '-');
        builder.Append(mode.HasFlag(UnixFileMode.UserWrite) ? 'w' : '-');
        builder.Append(mode.HasFlag(UnixFileMode.UserExecute) ? 'x' : '-');
        builder.Append(mode.HasFlag(UnixFileMode.GroupRead) ? 'r' : '-');
        builder.Append(mode.HasFlag(UnixFileMode.GroupWrite) ? 'w' : '-');
        builder.Append(mode.HasFlag(UnixFileMode.GroupExecute) ? 'x' : '-');
        builder.Append(mode.HasFlag(UnixFileMode.OtherRead) ? 'r' : '-');
        builder.Append(mode.HasFlag(UnixFileMode.OtherWrite) ? 'w' : '-');
        builder.Append(mode.HasFlag(UnixFileMode.OtherExecute) ? 'x' : '-');
        return builder.ToString();
    }
}
