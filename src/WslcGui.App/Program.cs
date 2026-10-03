using Aprillz.MewUI;

using WslcGui.App.Dialogs;
using WslcGui.App.Shell;
using WslcGui.Core;
using WslcGui.Engine.Cli;

// Composition root: the only place that knows which engine implementation is in use.
Win32Platform.Register();
Direct2DBackend.Register();

using var engine = new CliEngine();

MainWindow? mainWindow = null;
var ui = new UserInteraction(() => mainWindow);

var engineStatus = new EngineStatusViewModel(engine.Info);
using var containers = new ContainersViewModel(engine.Containers, engine.Lifecycle, engine.Info, ui, engine.Events);
var images = new ImagesViewModel(engine.Images, engine.Info, ui);

var dialogs = new DialogService(engine.Lifecycle, engine.Images, CliEngine.DescribeRun, ui, () => mainWindow);
dialogs.ContainerStarted += () => _ = containers.RefreshAsync();
dialogs.ImagePulled += () => _ = images.RefreshAsync();

// Last-resort handler: report instead of crashing the app.
Application.DispatcherUnhandledException += e =>
{
    ui.ShowError("Something went wrong", e.Exception);
    e.Handled = true;
};

mainWindow = new MainWindow(engineStatus, containers, images, dialogs);
Application.Run(mainWindow, () =>
{
    _ = engineStatus.RefreshAsync();
    mainWindow.Start();
});
