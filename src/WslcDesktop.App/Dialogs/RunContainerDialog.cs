using Aprillz.MewUI;
using Aprillz.MewUI.Controls;
using Aprillz.MewUI.Input;

using WslcDesktop.App.Controls;
using WslcDesktop.App.Pages;
using WslcDesktop.Core;

namespace WslcDesktop.App.Dialogs;

/// <summary>"Run a new container": image, name, command, ports, environment, mounts, with a live CLI preview.</summary>
internal sealed class RunContainerDialog : Window
{
    private readonly RunContainerViewModel _vm;

    /// <param name="imageCandidates">Local image names, for suggestions.</param>
    /// <param name="volumeCandidates">Named volumes, for suggestions (a Windows folder can always be typed instead).</param>
    public RunContainerDialog(RunContainerViewModel vm, Func<IEnumerable<string>> imageCandidates, Func<IEnumerable<string>> volumeCandidates)
    {
        _vm = vm;
        Title = "Run a new container";
        StartupLocation = WindowStartupLocation.CenterOwner;
        WindowSize = WindowSize.Resizable(680, 720);
        PreviewKeyDown += e =>
        {
            if (e.Key == Key.Escape && !_vm.IsBusy && !AutoComplete.IsAnyOpen)
            {
                e.Handled = true;
                Close();
            }
        };

        var image = AutoComplete.Attach(FormParts.Input(vm.Image, "Image, for example postgres:16-alpine", text => vm.Image = text), imageCandidates);
        var preview = new MultiLineTextBox().IsReadOnly().Wrap(true).Text(vm.CommandPreview).Height(64);
        vm.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(vm.CommandPreview))
            {
                preview.Text = vm.CommandPreview;
            }
        };

        var runButton = new Button().MinWidth(96).Content("Run").OnClick(() => _ = RunAsync());
        var cancelButton = new Button().MinWidth(96).Content("Cancel").OnClick(Close);
        vm.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(vm.IsBusy))
            {
                runButton.Content(vm.IsBusy ? "Starting…" : "Run");
                runButton.IsEnabled = !vm.IsBusy;
                cancelButton.IsEnabled = !vm.IsBusy;
            }
        };

        var form = new StackPanel().Vertical().Spacing(10).Children(
            FormParts.Field("Image", image),
            new Grid().Columns("*, *").Children(
                FormParts.Field("Name", FormParts.Input(vm.Name, "Optional (a name is generated)", text => vm.Name = text)).Margin(0, 0, 6, 0),
                FormParts.Field("Command", FormParts.Input(vm.Command, "Optional (overrides the image's command)", text => vm.Command = text)).Column(1).Margin(6, 0, 0, 0)),
            FormParts.RowList("Ports", "Add port", vm.Ports, () => new PortEntry(), port =>
                new Grid().Columns("*, *, Auto").Children(
                    FormParts.Input(port.HostPort, "Windows port (e.g. 8080)", text => port.HostPort = text).Margin(0, 0, 6, 0),
                    FormParts.Input(port.ContainerPort, "Container port (e.g. 80)", text => port.ContainerPort = text).Column(1).Margin(0, 0, 6, 0),
                    new CheckBox().Content("UDP").IsChecked(port.Udp).OnCheckedChanged(isChecked => port.Udp = isChecked).Column(2).CenterVertical())),
            FormParts.RowList("Environment variables", "Add variable", vm.Environment, () => new EnvironmentEntry(), variable =>
                new Grid().Columns("*, 2*").Children(
                    FormParts.Input(variable.Key, "NAME", text => variable.Key = text).Margin(0, 0, 6, 0),
                    FormParts.Input(variable.Value, "value", text => variable.Value = text).Column(1))),
            FormParts.RowList("Volumes", "Add volume", vm.Mounts, () => new MountEntry(), mount =>
            {
                var source = AutoComplete.Attach(
                    FormParts.Input(mount.Source, @"Windows folder (C:\data) or volume name", text => mount.Source = text),
                    text => Suggestions.LooksLikePath(text) ? [] : Suggestions.Filter(volumeCandidates(), text));
                var browse = new Button().Content("Browse…").Padding(8, 4).OnClick(() => _ = BrowseAsync(mount, source));
                return new Grid().Columns("2*, Auto, *, Auto").Children(
                    source.Margin(0, 0, 4, 0),
                    browse.Column(1).Margin(0, 0, 6, 0),
                    FormParts.Input(mount.Target, "/path/in/container", text => mount.Target = text).Column(2).Margin(0, 0, 6, 0),
                    new CheckBox().Content("Read-only").IsChecked(mount.ReadOnly).OnCheckedChanged(isChecked => mount.ReadOnly = isChecked).Column(3).CenterVertical());
            }),
            new CheckBox().Content("Remove the container when it exits").IsChecked(vm.AutoRemove).OnCheckedChanged(isChecked => vm.AutoRemove = isChecked).Margin(0, 8, 0, 0),
            FormParts.SectionTitle("Equivalent command"),
            new DockPanel().Spacing(6).Children(
                new Button().Content("Copy").Padding(8, 4).DockRight().OnClick(() => PageParts.CopyToClipboard(vm.CommandPreview)),
                preview));

        Content = new DockPanel().Children(
            new StackPanel().DockBottom().Vertical().Spacing(8).Padding(20, 8, 20, 16).Children(
                FormParts.BoundText(vm, nameof(vm.Error), x => x.Error, FormParts.ErrorColor),
                new StackPanel().Horizontal().Spacing(8).Right().Children(runButton, cancelButton)),
            new ScrollViewer().AutoVerticalScroll().NoHorizontalScroll().Padding(20, 16, 20, 8).Content(form));

        Loaded += () => FocusManager.SetFocus(image);
    }

    /// <summary>The new container's ID if one was started.</summary>
    public string? ContainerId { get; private set; }

    private async Task RunAsync()
    {
        ContainerId = await _vm.RunAsync();
        if (ContainerId is not null)
        {
            Close();
        }
    }

    private async Task BrowseAsync(MountEntry mount, TextBox source)
    {
        var folder = await FileDialog.SelectFolderAsync(new FolderDialogOptions { Owner = this, Title = "Folder to share with the container" });
        if (folder is not null)
        {
            mount.Source = folder;
            source.Text = folder;
        }
    }
}
