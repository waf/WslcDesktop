using Aprillz.MewUI;
using Aprillz.MewUI.Controls;
using Aprillz.MewUI.Input;
using Aprillz.MewUI.Rendering;

using WslcGui.App.Dialogs;
using WslcGui.App.Icons;
using WslcGui.Core;
using WslcGui.Engine;

namespace WslcGui.App.Pages;

/// <summary>One container: header with actions, and Logs / Inspect / Exec tabs.</summary>
internal sealed class ContainerDetailsView : UserControl
{
    private const string MonospaceFont = "Cascadia Mono";

    private readonly ContainerDetailsViewModel _vm;
    private readonly Action _goBack;
    private bool _inspectLoaded;

    public ContainerDetailsView(ContainerDetailsViewModel vm, Action goBack)
    {
        _vm = vm;
        _goBack = goBack;
        Build();
    }

    protected override Element OnBuild()
    {
        var files = new ContainerFilesTab(_vm.Files);
        var tabs = new TabControl()
            .Tab("Logs", BuildLogsTab())
            .Tab("Stats", new ContainerStatsTab(_vm))
            .Tab("Files", files)
            .Tab("Inspect", BuildInspectTab())
            .Tab("Exec", BuildExecTab());
        tabs.OnSelectionChanged(_ =>
        {
            switch (tabs.SelectedIndex)
            {
                case 2:
                    files.Activate();
                    break;
                case 3 when !_inspectLoaded:
                    _inspectLoaded = true;
                    _ = _vm.LoadInspectAsync();
                    break;
            }
        });

        _vm.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(_vm.IsRemoved) && _vm.IsRemoved)
            {
                _goBack();
            }
        };

        return new Grid()
            .Rows("Auto, *")
            .Children(
                BuildHeader(),
                tabs.Row(1).Margin(PageParts.Inset, 0, PageParts.Inset, 16));
    }

    private DockPanel BuildHeader()
    {
        var back = new Button()
            .StyleName("flat-button")
            .Padding(8, 6)
            .ToolTip("Back to containers")
            .Content(new TextBlock().Text("←").FontSize(16))
            .OnClick(_goBack);

        var dot = new Ellipse().Size(10, 10).CenterVertical();
        var name = new TextBlock().FontSize(ThemeFontSize.Medium).SemiBold().CenterVertical();
        var subtitle = new TextBlock().WithTheme((theme, t) => t.Foreground(theme.Palette.WindowText.WithAlpha(170)));

        var start = PageParts.ToolButton(IconData.Play, "Start", () => _ = _vm.StartAsync());
        var stop = PageParts.ToolButton(IconData.Stop, "Stop", () => _ = _vm.StopAsync());
        var restart = PageParts.ToolButton(IconData.ArrowClockwise, "Restart", () => _ = _vm.RestartAsync());
        var terminal = PageParts.ToolButton(IconData.WindowDevTools, "Open terminal", _vm.OpenTerminal);
        var remove = PageParts.ToolButton(IconData.Delete, "Remove", () => _ = _vm.RemoveAsync());

        void Update()
        {
            var row = _vm.Row;
            dot.Fill = new SolidColorBrush(row.IsRunning ? Color.FromRgb(46, 160, 67) : Color.FromRgb(140, 140, 140));
            name.Text = row.Name;
            var parts = new List<string> { row.Image, row.StatusText };
            if (row.PortsText.Length > 0)
            {
                parts.Add($"Ports {row.PortsText}");
            }

            parts.Add($"ID {row.ShortId}");
            subtitle.Text = string.Join("   ·   ", parts);
            start.IsVisible = !row.IsRunning;
            stop.IsVisible = row.IsRunning;
            restart.IsEnabled = row.Pending is null;
            terminal.IsEnabled = row.State == ContainerState.Running;
            terminal.ToolTip(row.State == ContainerState.Running ? "Open a shell in Windows Terminal" : "Start the container to open a terminal");
        }

        _vm.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(_vm.Row))
            {
                Update();
            }
        };
        Update();

        return new DockPanel()
            .Padding(8, 12, PageParts.Inset, 8)
            .Spacing(8)
            .Children(
                back.DockLeft().Top(),
                new StackPanel().DockRight().Horizontal().Spacing(4).Top().Children(start, stop, restart, terminal, remove),
                new StackPanel().Vertical().Spacing(4).Children(
                    new StackPanel().Horizontal().Spacing(8).Children(dot, name),
                    subtitle));
    }

    private DockPanel BuildLogsTab()
    {
        var logs = _vm.Logs;
        var view = MonospaceText();

        logs.TextReset += text => SetTextAndScrollToEnd(view, text);
        logs.TextAppended += text => view.AppendText(text, scrollToCaret: true);

        var filter = PageParts.SearchBox("Filter lines", text => logs.Filter = text);
        var timestamps = new CheckBox().Content("Timestamps").CenterVertical().OnCheckedChanged(isChecked => logs.ShowTimestamps = isChecked);
        var pause = new CheckBox().Content("Pause").CenterVertical().ToolTip("Stop updating the view so you can select text")
            .OnCheckedChanged(isChecked => logs.IsPaused = isChecked);
        var copy = new Button().Content("Copy all").Padding(10, 4).OnClick(() => PageParts.CopyToClipboard(logs.GetText()));
        var status = FormParts.BoundText(logs, nameof(logs.StatusText), x => x.StatusText).Margin(0, 0, 0, 6);

        logs.Start();

        return new DockPanel().Padding(0, 8, 0, 0).Children(
            new DockPanel().DockTop().Spacing(12).Margin(0, 0, 0, 8).Children(
                copy.DockRight(),
                new StackPanel().Horizontal().Spacing(12).Children(filter, timestamps, pause)),
            status.DockTop(),
            view);
    }

    private DockPanel BuildInspectTab()
    {
        var view = MonospaceText();
        _vm.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(_vm.InspectJson))
            {
                view.Text(_vm.InspectJson ?? string.Empty);
            }
            else if (e.PropertyName == nameof(_vm.IsInspectLoading) && _vm.IsInspectLoading && string.IsNullOrEmpty(_vm.InspectJson))
            {
                view.Text("Loading…");
            }
        };

        return new DockPanel().Padding(0, 8, 0, 0).Children(
            new StackPanel().DockTop().Horizontal().Spacing(8).Margin(0, 0, 0, 8).Children(
                new Button().Content("Refresh").Padding(10, 4).OnClick(() => _ = _vm.LoadInspectAsync()),
                new Button().Content("Copy").Padding(10, 4).OnClick(() => PageParts.CopyToClipboard(_vm.InspectJson ?? string.Empty))),
            view);
    }

    private DockPanel BuildExecTab()
    {
        var transcript = MonospaceText();
        var command = new TextBox()
            .Placeholder("Command to run in the container, for example: ls -la /")
            .FontFamily(MonospaceFont)
            .OnTextChanged(text => _vm.ExecCommand = text);
        var run = new Button().Content("Run").MinWidth(80).OnClick(() => _ = _vm.RunExecAsync());

        command.KeyDown += e =>
        {
            if (e.Key == Key.Enter)
            {
                e.Handled = true;
                _ = _vm.RunExecAsync();
            }
        };

        _vm.PropertyChanged += (_, e) =>
        {
            switch (e.PropertyName)
            {
                case nameof(_vm.ExecTranscript):
                    SetTextAndScrollToEnd(transcript, _vm.ExecTranscript);
                    break;
                case nameof(_vm.ExecCommand) when _vm.ExecCommand.Length == 0:
                    command.Text(string.Empty);
                    break;
                case nameof(_vm.IsExecBusy):
                    run.IsEnabled = !_vm.IsExecBusy;
                    run.Content(_vm.IsExecBusy ? "Running…" : "Run");
                    break;
            }
        };

        return new DockPanel().Padding(0, 8, 0, 0).Children(
            FormParts.Hint("Runs one command with 'wslc exec' and shows its output. For an interactive shell, use Open terminal.").DockTop().Margin(0, 0, 0, 8),
            new DockPanel().DockBottom().Spacing(8).Margin(0, 8, 0, 0).Children(run.DockRight(), command),
            transcript);
    }

    private static void SetTextAndScrollToEnd(MultiLineTextBox view, string text)
    {
        view.Text(text);
        view.MoveCaret(view.Document.TextLength, extendSelection: false);
        view.ScrollToCaret();
    }

    private static MultiLineTextBox MonospaceText()
    {
        var text = new MultiLineTextBox().IsReadOnly().Wrap(false).FontFamily(MonospaceFont);
        text.Document.UndoSizeLimit = 0;
        return text;
    }
}
