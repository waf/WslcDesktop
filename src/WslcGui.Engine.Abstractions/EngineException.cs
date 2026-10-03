namespace WslcGui.Engine;

public enum EngineErrorKind
{
    Unknown,
    /// <summary>The engine (wslc / WSL service) is missing, not installed, or failed to start.</summary>
    Unavailable,
    NotFound,
    /// <summary>The object is in the wrong state or already exists (for example removing a running container).</summary>
    Conflict,
    NotRunning,
    InvalidArgument,
    /// <summary>The operation isn't possible for this object (for example listing files in an image without a shell).</summary>
    NotSupported,
    Cancelled,
}

/// <summary>The one exception type engines raise. Implementations map their own failures (CLI stderr, HRESULTs) into it.</summary>
public sealed class EngineException : Exception
{
    public EngineException(EngineErrorKind kind, string message, Exception? innerException = null)
        : base(message, innerException)
    {
        Kind = kind;
    }

    public EngineErrorKind Kind { get; }

    /// <summary>The implementation's symbolic error code if it has one (for example <c>WSLC_E_CONTAINER_NOT_FOUND</c>).</summary>
    public string? Code { get; init; }

    /// <summary>Raw diagnostic detail from the implementation (for example the CLI command and its stderr).</summary>
    public string? Detail { get; init; }
}
