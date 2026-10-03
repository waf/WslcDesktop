using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading.Channels;

namespace WslcGui.Engine.Cli;

/// <param name="ExecutablePath">Path to wslc.exe; null to find it in the WSL install folder or on PATH.</param>
/// <param name="Session">The session to target (<c>--session</c>); null for the CLI's default per-user session.</param>
public sealed record WslcCliOptions(string? ExecutablePath = null, string? Session = null);

/// <summary>
/// The single place in the app that starts processes. Every wslc invocation goes through here, which gives one
/// spot for argument handling, encoding, cancellation, child-process cleanup, concurrency limits and tracing.
/// </summary>
internal sealed class WslcCli : ICliRunner, IDisposable
{
    private const int MaxConcurrentShortCommands = 4;

    private readonly WslcCliOptions _options;
    private readonly Lazy<string?> _executable;
    private readonly SemaphoreSlim _concurrency = new(MaxConcurrentShortCommands);
    private readonly JobObject _job = new();

    public WslcCli(WslcCliOptions options)
    {
        _options = options;
        _executable = new Lazy<string?>(() => ResolveExecutable(options.ExecutablePath));
    }

    /// <summary>Raised (on a background thread) after every short command completes. Used for diagnostics.</summary>
    public event Action<CliResult>? CommandCompleted;

    public Task<CliResult> RunAsync(IReadOnlyList<string> arguments, CancellationToken cancellationToken) =>
        RunCoreAsync(arguments, standardInput: null, cancellationToken);

    public Task<CliResult> RunWithInputAsync(IReadOnlyList<string> arguments, string standardInput, CancellationToken cancellationToken) =>
        RunCoreAsync(arguments, standardInput, cancellationToken);

    private async Task<CliResult> RunCoreAsync(IReadOnlyList<string> arguments, string? standardInput, CancellationToken cancellationToken)
    {
        await _concurrency.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var stopwatch = Stopwatch.StartNew();
            using var process = Start(arguments, redirectInput: standardInput is not null);
            using var registration = KillOnCancel(process, cancellationToken);
            if (standardInput is not null)
            {
                // Secrets go through stdin so they never appear in a process command line.
                await process.StandardInput.WriteAsync(standardInput).ConfigureAwait(false);
                process.StandardInput.Close();
            }

            var stdout = process.StandardOutput.ReadToEndAsync(CancellationToken.None);
            var stderr = process.StandardError.ReadToEndAsync(CancellationToken.None);
            await process.WaitForExitAsync(CancellationToken.None).ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();

            var result = new CliResult(arguments, process.ExitCode, await stdout.ConfigureAwait(false), await stderr.ConfigureAwait(false), stopwatch.Elapsed);
            CommandCompleted?.Invoke(result);
            return result;
        }
        finally
        {
            _concurrency.Release();
        }
    }

    public async IAsyncEnumerable<string> StreamLinesAsync(IReadOnlyList<string> arguments, [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var stopwatch = Stopwatch.StartNew();
        using var process = Start(arguments);
        using var registration = KillOnCancel(process, cancellationToken);

        var stderr = process.StandardError.ReadToEndAsync(CancellationToken.None);
        try
        {
            while (await process.StandardOutput.ReadLineAsync(CancellationToken.None).ConfigureAwait(false) is { } line)
            {
                cancellationToken.ThrowIfCancellationRequested();
                yield return line;
            }

            await process.WaitForExitAsync(CancellationToken.None).ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();

            var result = new CliResult(arguments, process.ExitCode, string.Empty, await stderr.ConfigureAwait(false), stopwatch.Elapsed);
            CommandCompleted?.Invoke(result);
            if (result.ExitCode != 0)
            {
                throw CliErrors.FromResult(result);
            }
        }
        finally
        {
            // Reached on early exit from the consumer's loop as well as on completion.
            KillQuietly(process);
        }
    }

    public async IAsyncEnumerable<CliOutputLine> StreamOutputAsync(IReadOnlyList<string> arguments, [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var stopwatch = Stopwatch.StartNew();
        using var process = Start(arguments);
        using var registration = KillOnCancel(process, cancellationToken);

        // Both pipes are read concurrently into one channel so lines come out in arrival order.
        var channel = Channel.CreateUnbounded<CliOutputLine>(new UnboundedChannelOptions { SingleReader = true });
        var stdout = PumpAsync(process.StandardOutput, CliOutputKind.StdOut, channel.Writer);
        var stderr = PumpAsync(process.StandardError, CliOutputKind.StdErr, channel.Writer);
        _ = Task.WhenAll(stdout, stderr).ContinueWith(_ => channel.Writer.TryComplete(), TaskScheduler.Default);

        try
        {
            await foreach (var line in channel.Reader.ReadAllAsync(CancellationToken.None).ConfigureAwait(false))
            {
                cancellationToken.ThrowIfCancellationRequested();
                yield return line;
            }

            await process.WaitForExitAsync(CancellationToken.None).ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
            CommandCompleted?.Invoke(new CliResult(arguments, process.ExitCode, string.Empty, string.Empty, stopwatch.Elapsed));
            yield return new CliOutputLine(CliOutputKind.Exit, string.Empty, process.ExitCode);
        }
        finally
        {
            KillQuietly(process);
        }

        static async Task PumpAsync(StreamReader reader, CliOutputKind kind, ChannelWriter<CliOutputLine> writer)
        {
            while (await reader.ReadLineAsync(CancellationToken.None).ConfigureAwait(false) is { } line)
            {
                await writer.WriteAsync(new CliOutputLine(kind, line), CancellationToken.None).ConfigureAwait(false);
            }
        }
    }

    public string? ExecutablePath => _executable.Value;

    public IReadOnlyList<string> GlobalArguments => _options.Session is { } session ? ["--session", session] : [];

    public void LaunchDetached(string executable, IReadOnlyList<string> arguments, bool hidden = false)
    {
        var startInfo = new ProcessStartInfo(executable)
        {
            UseShellExecute = false,
            CreateNoWindow = hidden,
        };
        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        try
        {
            using var process = Process.Start(startInfo);
        }
        catch (Win32Exception ex)
        {
            throw new EngineException(EngineErrorKind.Unavailable, $"Could not start {executable}: {ex.Message}", ex);
        }
    }

    public void Dispose()
    {
        _job.Dispose();
        _concurrency.Dispose();
    }

    private Process Start(IReadOnlyList<string> arguments, bool redirectInput = false)
    {
        var executable = _executable.Value
            ?? throw new EngineException(EngineErrorKind.Unavailable, "wslc.exe was not found. Install or update WSL (wsl --update).");

        var startInfo = new ProcessStartInfo(executable)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            RedirectStandardInput = redirectInput,
            StandardInputEncoding = redirectInput ? new UTF8Encoding(encoderShouldEmitUTF8Identifier: false) : null,
            StandardOutputEncoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
            StandardErrorEncoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
            UseShellExecute = false,
            CreateNoWindow = true,
        };

        if (_options.Session is { } session)
        {
            startInfo.ArgumentList.Add("--session");
            startInfo.ArgumentList.Add(session);
        }

        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        try
        {
            var process = Process.Start(startInfo)
                ?? throw new EngineException(EngineErrorKind.Unavailable, $"Could not start {executable}.");
            _job.TryAssign(process.Handle);
            return process;
        }
        catch (Win32Exception ex)
        {
            throw new EngineException(EngineErrorKind.Unavailable, $"Could not start {executable}: {ex.Message}", ex);
        }
    }

    private static CancellationTokenRegistration KillOnCancel(Process process, CancellationToken cancellationToken) =>
        cancellationToken.Register(static state => KillQuietly((Process)state!), process);

    private static void KillQuietly(Process process)
    {
        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }
        }
        catch (InvalidOperationException)
        {
            // Already exited.
        }
        catch (Win32Exception)
        {
            // Exiting or access denied; the job object cleans up on app exit regardless.
        }
    }

    private static string? ResolveExecutable(string? configured)
    {
        if (!string.IsNullOrEmpty(configured))
        {
            return File.Exists(configured) ? configured : null;
        }

        var installed = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "WSL", "wslc.exe");
        if (File.Exists(installed))
        {
            return installed;
        }

        foreach (var directory in (Environment.GetEnvironmentVariable("PATH") ?? string.Empty).Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            var candidate = Path.Combine(directory.Trim(), "wslc.exe");
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        return null;
    }
}
