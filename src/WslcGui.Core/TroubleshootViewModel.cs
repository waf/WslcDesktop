using System.Collections.ObjectModel;
using System.Globalization;
using System.Text;

using WslcGui.Engine;

namespace WslcGui.Core;

/// <param name="Command">The command line as run (for example "wslc container list --all").</param>
/// <param name="Error">First line of the error output for failed commands.</param>
public sealed record CommandLogEntry(DateTimeOffset Time, string Command, int ExitCode, TimeSpan Duration, string? Error)
{
    public string TimeText => Time.ToLocalTime().ToString("HH:mm:ss", CultureInfo.InvariantCulture);
    public string DurationText => Duration.TotalMilliseconds < 1000 ? $"{Duration.TotalMilliseconds:0} ms" : $"{Duration.TotalSeconds:0.0} s";
    public string ResultText => ExitCode == 0 ? "OK" : $"exit {ExitCode}" + (Error is { } e ? $": {e}" : string.Empty);
}

/// <summary>Engine details, the recent engine command log, and recovery actions.</summary>
public sealed class TroubleshootViewModel(IEngineMaintenance maintenance, IUserInteraction ui) : ObservableObject
{
    public const int MaxLogEntries = 300;

    private IReadOnlyList<KeyValuePair<string, string>> _details = [];
    private string? _detailsError;

    public IReadOnlyList<KeyValuePair<string, string>> Details
    {
        get => _details;
        private set => SetProperty(ref _details, value);
    }

    public string? DetailsError
    {
        get => _detailsError;
        private set => SetProperty(ref _detailsError, value);
    }

    /// <summary>Newest first.</summary>
    public ObservableCollection<CommandLogEntry> RecentCommands { get; } = [];

    /// <summary>Records a completed engine command. Call on the UI thread.</summary>
    public void Record(CommandLogEntry entry)
    {
        RecentCommands.Insert(0, entry);
        while (RecentCommands.Count > MaxLogEntries)
        {
            RecentCommands.RemoveAt(RecentCommands.Count - 1);
        }
    }

    public async Task RefreshAsync()
    {
        try
        {
            Details = await maintenance.GetDetailsAsync();
            DetailsError = null;
        }
        catch (EngineException ex)
        {
            DetailsError = ex.Message;
        }
    }

    public void OpenEngineSettings()
    {
        try
        {
            maintenance.OpenSettingsFile();
        }
        catch (EngineException ex)
        {
            ui.ShowError("Couldn't open the WSLC settings file", ex);
        }
    }

    public async Task RestartEngineAsync()
    {
        if (!await ui.ConfirmAsync("Restart the engine", "Restart the WSLC engine? All running containers stop. The engine starts again on the next request.", "Restart"))
        {
            return;
        }

        try
        {
            await maintenance.RestartEngineAsync();
            ui.ShowToast("The engine was stopped. It starts again on the next request.");
        }
        catch (EngineException ex)
        {
            ui.ShowError("Couldn't restart the engine", ex);
        }

        await RefreshAsync();
    }

    /// <summary>Details and recent commands as text, for bug reports.</summary>
    public string GetDiagnosticsText()
    {
        var builder = new StringBuilder();
        builder.AppendLine("WSLC Desktop diagnostics").AppendLine();
        foreach (var (key, value) in Details)
        {
            builder.Append(key).Append(": ").AppendLine(value);
        }

        builder.AppendLine().AppendLine("Recent commands (newest first):");
        foreach (var entry in RecentCommands)
        {
            builder.Append(entry.TimeText).Append("  ").Append(entry.DurationText.PadLeft(8)).Append("  ")
                .Append(entry.ResultText).Append("  ").AppendLine(entry.Command);
        }

        return builder.ToString();
    }
}
