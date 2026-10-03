using System.Text.Json.Serialization;

namespace WslcGui.Engine.Cli;

internal sealed class CliEngineInfo(ICliRunner cli) : IEngineInfo
{
    public async Task<EngineVersion> GetVersionAsync(CancellationToken cancellationToken = default)
    {
        var version = await cli.RunJsonAsync(["version", "--format", "json"], CliJsonContext.Default.VersionDto, cancellationToken).ConfigureAwait(false);
        return new EngineVersion(version.Client?.Version ?? "unknown");
    }

    public async Task<EngineHealth> CheckHealthAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var version = await GetVersionAsync(cancellationToken).ConfigureAwait(false);
            return new EngineHealth(EngineHealthStatus.Ready, version, null);
        }
        catch (EngineException ex) when (ex.Kind == EngineErrorKind.Unavailable)
        {
            return new EngineHealth(EngineHealthStatus.NotInstalled, null, ex.Message);
        }
        catch (EngineException ex)
        {
            return new EngineHealth(EngineHealthStatus.Error, null, ex.Message);
        }
    }
}

// `wslc version --format json` => {"Client":{"Version":"3.0.1.0"}}
internal sealed record VersionDto(VersionClientDto? Client);

internal sealed record VersionClientDto(string? Version);

[JsonSourceGenerationOptions(PropertyNameCaseInsensitive = true)]
[JsonSerializable(typeof(VersionDto))]
internal sealed partial class CliJsonContext : JsonSerializerContext;
