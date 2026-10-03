using System.Text.Json;
using System.Text.Json.Serialization;

namespace WslcDesktop.App.Shell;

public enum AppTheme
{
    System,
    Light,
    Dark,
}

/// <summary>The app's own preferences (the engine has its own settings file).</summary>
internal sealed record AppSettings
{
    public AppTheme Theme { get; init; } = AppTheme.System;

    /// <summary>Closing the window keeps the app running in the notification area.</summary>
    public bool CloseToTray { get; init; }

    private static string FilePath => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "WslcDesktop", "settings.json");

    public static AppSettings Load()
    {
        try
        {
            return File.Exists(FilePath)
                ? JsonSerializer.Deserialize(File.ReadAllText(FilePath), AppSettingsJsonContext.Default.AppSettings) ?? new AppSettings()
                : new AppSettings();
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            return new AppSettings();
        }
    }

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(this, AppSettingsJsonContext.Default.AppSettings));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Preferences are best effort.
        }
    }
}

[JsonSourceGenerationOptions(WriteIndented = true, UseStringEnumConverter = true)]
[JsonSerializable(typeof(AppSettings))]
internal sealed partial class AppSettingsJsonContext : JsonSerializerContext;
