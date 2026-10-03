using Aprillz.MewUI;
using Aprillz.MewUI.Controls;
using Aprillz.MewUI.Input;

using WslcGui.Core;

namespace WslcGui.App.Dialogs;

/// <summary>"Build an image": context folder, Dockerfile, tag, build arguments, and the live build output.</summary>
internal sealed class BuildImageDialog : Window
{
    private readonly BuildImageViewModel _vm;

    public BuildImageDialog(BuildImageViewModel vm)
    {
        _vm = vm;
        Title = "Build an image";
        StartupLocation = WindowStartupLocation.CenterOwner;
        WindowSize = WindowSize.Resizable(760, 760);

        var context = FormParts.Input(vm.ContextDirectory, @"Folder with the Dockerfile, e.g. C:\src\myapp", text => vm.ContextDirectory = text);
        var dockerfile = FormParts.Input(vm.Dockerfile, "Optional: a Dockerfile other than <folder>\\Dockerfile", text => vm.Dockerfile = text);
        var build = new Button().MinWidth(96).Content("Build").OnClick(() => _ = vm.BuildAsync());
        var close = new Button().MinWidth(96).Content("Close").OnClick(CloseOrCancel);
        vm.Output.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(vm.Output.IsBusy))
            {
                build.IsEnabled = !vm.Output.IsBusy;
                close.Content(vm.Output.IsBusy ? "Cancel" : "Close");
            }
        };

        PreviewKeyDown += e =>
        {
            if (e.Key == Key.Escape)
            {
                e.Handled = true;
                CloseOrCancel();
            }
        };

        var form = new StackPanel().Vertical().Spacing(10).Children(
            FormParts.Field("Build context", new DockPanel().Spacing(6).Children(
                new Button().Content("Browse…").Padding(8, 4).DockRight().OnClick(() => _ = BrowseFolderAsync(context)),
                context)),
            FormParts.Field("Dockerfile", new DockPanel().Spacing(6).Children(
                new Button().Content("Browse…").Padding(8, 4).DockRight().OnClick(() => _ = BrowseFileAsync(dockerfile)),
                dockerfile)),
            FormParts.Field("Tag", FormParts.Input(vm.Tag, "Optional, e.g. myapp:dev", text => vm.Tag = text)),
            FormParts.RowList("Build arguments", "Add argument", vm.BuildArgs, () => new EnvironmentEntry(), arg =>
                new Grid().Columns("*, 2*").Children(
                    FormParts.Input(arg.Key, "NAME", text => arg.Key = text).Margin(0, 0, 6, 0),
                    FormParts.Input(arg.Value, "value", text => arg.Value = text).Column(1))),
            new StackPanel().Horizontal().Spacing(16).Children(
                new CheckBox().Content("Don't use the build cache").OnCheckedChanged(isChecked => vm.NoCache = isChecked),
                new CheckBox().Content("Always pull base images").OnCheckedChanged(isChecked => vm.Pull = isChecked)),
            FormParts.BoundText(vm, nameof(vm.Error), x => x.Error, FormParts.ErrorColor));

        Content = new DockPanel().Padding(20).Children(
            form.DockTop(),
            new StackPanel().DockBottom().Horizontal().Spacing(8).Right().Margin(0, 12, 0, 0).Children(build, close),
            CommandOutputDialog.BuildOutputArea(vm.Output).Margin(0, 12, 0, 0));

        Loaded += () => FocusManager.SetFocus(context);
    }

    /// <summary>Whether a build succeeded while the dialog was open.</summary>
    public bool Built => _vm.Output.Succeeded == true;

    private async Task BrowseFolderAsync(TextBox target)
    {
        if (await FileDialog.SelectFolderAsync(new FolderDialogOptions { Owner = this, Title = "Build context folder" }) is { } folder)
        {
            target.Text(folder);
            _vm.ContextDirectory = folder;
        }
    }

    private async Task BrowseFileAsync(TextBox target)
    {
        if (await FileDialog.OpenFileAsync(new OpenFileDialogOptions { Owner = this, Title = "Dockerfile" }) is { } file)
        {
            target.Text(file);
            _vm.Dockerfile = file;
        }
    }

    private void CloseOrCancel()
    {
        if (_vm.Output.IsBusy)
        {
            _vm.Output.Cancel();
        }
        else
        {
            Close();
        }
    }
}
