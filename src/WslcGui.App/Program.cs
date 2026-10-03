using Aprillz.MewUI;

using WslcGui.App.Dialogs;
using WslcGui.App.Pages;
using WslcGui.App.Shell;
using WslcGui.Core;
using WslcGui.Engine.Cli;

// Composition root: the only place that knows which engine implementation is in use.
Win32Platform.Register();
Direct2DBackend.Register();

// WSLCGUI_WSLC_PATH points at a different wslc.exe (a dev build, or a missing path to test the not-installed state).
using var engine = new CliEngine(new WslcCliOptions(ExecutablePath: Environment.GetEnvironmentVariable("WSLCGUI_WSLC_PATH") is { Length: > 0 } path ? path : null));
var settings = AppSettings.Load();

MainWindow? mainWindow = null;
var ui = new UserInteraction(() => mainWindow);

var engineStatus = new EngineStatusViewModel(engine.Info);

// The stats monitor and the containers list feed each other: stats are only sampled while containers run,
// and the list shows the latest sample.
ContainersViewModel? containersRef = null;
using var stats = new StatsMonitor(engine.Stats, engine.Info, () => containersRef?.HasRunningContainers == true);
using var containers = new ContainersViewModel(engine.Containers, engine.Lifecycle, engine.Info, ui, engine.Events, latestStats: () => stats.Latest);
containersRef = containers;
stats.Updated += () => containers.ApplyStats(stats.Latest);

var images = new ImagesViewModel(engine.Images, engine.Info, ui);
var volumes = new VolumesViewModel(engine.Volumes, engine.Containers, engine.Info, ui);
var networks = new NetworksViewModel(engine.Networks, engine.Info, ui);
var troubleshoot = new TroubleshootViewModel(engine.Maintenance, ui);

// Every wslc call lands in the Troubleshoot page's log. Calls complete on background threads.
engine.CommandCompleted += trace =>
{
    var entry = new CommandLogEntry(
        DateTimeOffset.Now - trace.Duration,
        "wslc " + string.Join(' ', trace.Arguments.Select(a => a.Contains(' ', StringComparison.Ordinal) ? $"\"{a}\"" : a)),
        trace.ExitCode,
        trace.Duration,
        trace.ExitCode == 0 ? null : trace.StdErr.Split('\n').Select(l => l.Trim()).FirstOrDefault(l => l.Length > 0));
    if (Application.IsRunning)
    {
        Application.Current.Dispatcher?.BeginInvoke(() => troubleshoot.Record(entry));
    }
};

// Suggestions come from lists the pages have already loaded, so opening a dialog never starts the engine VM.
IEnumerable<string> ImageCandidates() => images.AllItems.Where(i => i.Repository != "<none>").Select(i => i.Reference);
IEnumerable<string> VolumeCandidates() => volumes.AllItems.Where(v => !v.IsAnonymous).Select(v => v.Name);

var dialogs = new DialogService(engine.Lifecycle, engine.Images, engine.Registry, ImageCandidates, VolumeCandidates, CliEngine.DescribeRun, ui, () => mainWindow);
dialogs.ContainerStarted += () => _ = containers.RefreshAsync();
dialogs.ImagePulled += () => _ = images.RefreshAsync();
dialogs.ImageBuilt += () => _ = images.RefreshAsync();

// Last-resort handler: report instead of crashing the app.
Application.DispatcherUnhandledException += e =>
{
    ui.ShowError("Something went wrong", e.Exception);
    e.Handled = true;
};

void ApplySettings(AppSettings updated)
{
    settings = updated;
    settings.Save();
    Application.Current.SetTheme(settings.Theme switch
    {
        AppTheme.Light => ThemeVariant.Light,
        AppTheme.Dark => ThemeVariant.Dark,
        _ => ThemeVariant.System,
    });
}

ContainerDetailsViewModel CreateContainerDetails(ContainerRow row) =>
    new(row, containers, engine.Containers, engine.Logs, engine.Exec, engine.ExternalTerminal, engine.Files, stats, ui);

var pages = new MainWindowPages(
    new ContainersPage(containers, dialogs, CreateContainerDetails, stats),
    new ImagesPage(images, dialogs),
    new VolumesPage(volumes, dialogs),
    new NetworksPage(networks, dialogs),
    new TroubleshootPage(troubleshoot),
    new SettingsPage(() => settings, ApplySettings, troubleshoot, engineStatus));

mainWindow = new MainWindow(engineStatus, containers, pages, () => settings);
Application.Run(mainWindow, () =>
{
    ApplySettings(settings);
    _ = engineStatus.RefreshAsync();
    mainWindow.Start();
});
