using System.Text.Json.Serialization;

namespace WslcGui.Engine.Cli;

// DTOs mirror wslc's own output and stay internal to this project; they're mapped to WslcGui.Engine models.
// List output values are Docker-template strings (see docs/spikes/S1-cli-contract.md §2).

/// <summary><c>version --format json</c>: <c>{"Client":{"Version":"3.0.1.0"}}</c></summary>
internal sealed record VersionDto(VersionClientDto? Client);

internal sealed record VersionClientDto(string? Version);

/// <summary>One line of <c>container list --format json</c>.</summary>
internal sealed record ContainerListDto(
    string? ID,
    string? Names,
    string? Image,
    string? Command,
    string? CreatedAt,
    string? State,
    string? Status,
    string? Ports);

/// <summary>One line of <c>image list --format json</c>.</summary>
internal sealed record ImageListDto(
    string? ID,
    string? Repository,
    string? Tag,
    string? Size,
    string? CreatedAt,
    string? Containers);

/// <summary><c>events --format json</c> (WSL after 3.0.1; Docker-shaped). 3.0.1 only prints text, see <see cref="CliEventSource"/>.</summary>
internal sealed record EventDto(string? Type, string? Action, EventActorDto? Actor, long? Time, long? TimeNano);

internal sealed record EventActorDto(string? ID, Dictionary<string, string>? Attributes);

[JsonSourceGenerationOptions(PropertyNameCaseInsensitive = true)]
[JsonSerializable(typeof(EventDto))]
[JsonSerializable(typeof(VersionDto))]
[JsonSerializable(typeof(ContainerListDto))]
[JsonSerializable(typeof(ImageListDto))]
internal sealed partial class CliJsonContext : JsonSerializerContext;
