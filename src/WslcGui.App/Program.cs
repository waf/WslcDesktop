using Aprillz.MewUI;

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
var containers = new ContainersViewModel(engine.Containers, engine.Lifecycle, engine.Info, ui);
var images = new ImagesViewModel(engine.Images, engine.Info, ui);

mainWindow = new MainWindow(engineStatus, containers, images);
Application.Run(mainWindow, () =>
{
    _ = engineStatus.RefreshAsync();
    mainWindow.Start();
});
