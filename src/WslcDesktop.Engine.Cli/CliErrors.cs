namespace WslcDesktop.Engine.Cli;

/// <summary>
/// Maps failed wslc invocations to <see cref="EngineException"/>. All CLI errors exit 1 and write to stderr:
/// <code>
/// Container 'x' not found.
/// Error code: WSLC_E_CONTAINER_NOT_FOUND
/// If this error was unexpected, please consider searching for existing issues or filing a new issue at https://github.com/microsoft/WSL/issues.
/// </code>
/// Some errors (inspect not-found, argument parsing) have no code line.
/// </summary>
internal static class CliErrors
{
    private const string CodePrefix = "Error code:";
    private const string BoilerplatePrefix = "If this error was unexpected";

    private static readonly Dictionary<string, EngineErrorKind> s_codes = new(StringComparer.Ordinal)
    {
        ["WSLC_E_CONTAINER_NOT_FOUND"] = EngineErrorKind.NotFound,
        ["WSLC_E_IMAGE_NOT_FOUND"] = EngineErrorKind.NotFound,
        ["WSLC_E_VOLUME_NOT_FOUND"] = EngineErrorKind.NotFound,
        ["WSLC_E_NETWORK_NOT_FOUND"] = EngineErrorKind.NotFound,
        ["WSLC_E_SESSION_NOT_FOUND"] = EngineErrorKind.Unavailable,
        ["WSLC_E_VM_NOT_RUNNING"] = EngineErrorKind.Unavailable,
        ["WSLC_E_CONTAINER_NOT_RUNNING"] = EngineErrorKind.NotRunning,
        ["WSLC_E_CONTAINER_IS_RUNNING"] = EngineErrorKind.Conflict,
        ["WSLC_E_CONTAINER_PREFIX_AMBIGUOUS"] = EngineErrorKind.InvalidArgument,
        ["WSLC_E_INVALID_SESSION_NAME"] = EngineErrorKind.InvalidArgument,
        ["ERROR_ALREADY_EXISTS"] = EngineErrorKind.Conflict,
        ["ERROR_PATH_NOT_FOUND"] = EngineErrorKind.NotFound,
        ["ERROR_FILE_NOT_FOUND"] = EngineErrorKind.NotFound,
        ["E_INVALIDARG"] = EngineErrorKind.InvalidArgument,
    };

    public static EngineException FromResult(CliResult result)
    {
        var message = new List<string>();
        string? code = null;

        foreach (var rawLine in (result.StdErr.Length > 0 ? result.StdErr : result.StdOut).Split('\n'))
        {
            var line = rawLine.Trim();
            if (line.StartsWith(CodePrefix, StringComparison.Ordinal))
            {
                code = line[CodePrefix.Length..].Trim();
            }
            else if (line.Length > 0 && !line.StartsWith(BoilerplatePrefix, StringComparison.Ordinal))
            {
                message.Add(line);
            }
        }

        var text = message.Count > 0 ? string.Join(' ', message)
            : code is not null ? $"wslc failed with {code}."
            : $"wslc exited with code {result.ExitCode}.";
        return new EngineException(Classify(code, text), text)
        {
            Code = code,
            Detail = $"wslc {string.Join(' ', result.Arguments)}\nexit code {result.ExitCode}\n{result.StdErr}",
        };
    }

    private static EngineErrorKind Classify(string? code, string message)
    {
        if (code is not null && s_codes.TryGetValue(code, out var kind))
        {
            return kind;
        }

        // No (or unknown) code: inspect not-found and argument errors only have a message.
        if (message.Contains("not found", StringComparison.OrdinalIgnoreCase))
        {
            return EngineErrorKind.NotFound;
        }

        if (message.Contains("not recognized", StringComparison.OrdinalIgnoreCase))
        {
            return EngineErrorKind.InvalidArgument;
        }

        return EngineErrorKind.Unknown;
    }
}
