using Aprillz.MewUI;

using WslcGui.App.Shell;
using WslcGui.Core;
using WslcGui.Engine.Cli;

// Composition root: the only place that knows which engine implementation is in use.
Win32Platform.Register();
Direct2DBackend.Register();

using var engine = new CliEngine();
var engineStatus = new EngineStatusViewModel(engine.Info);

var mainWindow = new MainWindow(engineStatus);
Application.Run(mainWindow, () => _ = engineStatus.RefreshAsync());
