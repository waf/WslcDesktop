using System.Text.Json;

namespace WslcGui.Engine.Cli;

internal sealed class CliRegistryService(ICliRunner cli) : IRegistryService
{
    public async Task LoginAsync(string? server, string username, string password, CancellationToken cancellationToken = default)
    {
        List<string> arguments = ["registry", "login", "--username", username, "--password-stdin"];
        if (server is { Length: > 0 })
        {
            arguments.Add(server);
        }

        var result = await cli.RunWithInputAsync(arguments, password, cancellationToken).ConfigureAwait(false);
        if (result.ExitCode != 0)
        {
            throw CliErrors.FromResult(result);
        }
    }

    public Task LogoutAsync(string? server, CancellationToken cancellationToken = default) =>
        cli.RunCheckedAsync(server is { Length: > 0 } ? ["registry", "logout", server] : ["registry", "logout"], cancellationToken);
}

internal sealed class CliEngineMaintenance(ICliRunner cli) : IEngineMaintenance
{
    /// <summary>Flattens <c>info --format json</c> (which doesn't start the VM, S1 §2.5) into display rows.</summary>
    public async Task<IReadOnlyList<KeyValuePair<string, string>>> GetDetailsAsync(CancellationToken cancellationToken = default)
    {
        var result = await cli.RunCheckedAsync(["info", "--format", "json"], cancellationToken).ConfigureAwait(false);
        var rows = new List<KeyValuePair<string, string>>();
        try
        {
            using var document = JsonDocument.Parse(result.StdOut);
            Flatten(document.RootElement, string.Empty, rows);
        }
        catch (JsonException ex)
        {
            throw new EngineException(EngineErrorKind.Unknown, "Unexpected output from 'wslc info'.", ex) { Detail = result.StdOut };
        }

        if (cli.ExecutablePath is { } path)
        {
            rows.Insert(0, new("wslc", path));
        }

        return rows;
    }

    public void OpenSettingsFile()
    {
        // "wslc settings" creates the file with commented defaults on first use and opens it in the default editor.
        var wslc = cli.ExecutablePath
            ?? throw new EngineException(EngineErrorKind.Unavailable, "wslc.exe was not found. Install or update WSL (wsl --update).");
        cli.LaunchDetached(wslc, [.. cli.GlobalArguments, "settings"], hidden: true);
    }

    public Task RestartEngineAsync(CancellationToken cancellationToken = default) =>
        cli.RunCheckedAsync(["system", "session", "terminate"], cancellationToken);

    internal static void Flatten(JsonElement element, string prefix, List<KeyValuePair<string, string>> rows)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                foreach (var property in element.EnumerateObject())
                {
                    Flatten(property.Value, prefix.Length == 0 ? property.Name : $"{prefix}.{property.Name}", rows);
                }

                break;
            case JsonValueKind.Array:
                var index = 0;
                foreach (var item in element.EnumerateArray())
                {
                    Flatten(item, $"{prefix}[{index++}]", rows);
                }

                if (index == 0)
                {
                    rows.Add(new(prefix, "(none)"));
                }

                break;
            default:
                rows.Add(new(prefix, element.ToString()));
                break;
        }
    }
}
