using Aprillz.MewUI;
using Aprillz.MewUI.Controls;
using Aprillz.MewUI.Input;

using WslcGui.Core;

namespace WslcGui.App.Dialogs;

internal sealed class PullImageDialog : Window
{
    private readonly PullImageViewModel _vm;

    public PullImageDialog(PullImageViewModel vm)
    {
        _vm = vm;
        Title = "Pull an image";
        StartupLocation = WindowStartupLocation.CenterOwner;
        WindowSize = WindowSize.FitContentSize(520, 320);

        var reference = FormParts.Input(vm.Reference, "alpine, redis:7-alpine, ghcr.io/owner/image:tag", text => vm.Reference = text);
        var progress = new ProgressBar { IsIndeterminate = true }.IsVisible(false);
        var pullButton = new Button().MinWidth(96).Content("Pull").OnClick(() => _ = PullAsync());
        var closeButton = new Button().MinWidth(96).Content("Close").OnClick(CloseOrCancel);

        vm.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(vm.IsBusy))
            {
                progress.IsVisible = vm.IsBusy;
                pullButton.IsEnabled = !vm.IsBusy;
                reference.IsEnabled = !vm.IsBusy;
                closeButton.Content(vm.IsBusy ? "Cancel" : "Close");
            }
        };

        PreviewKeyDown += e =>
        {
            if (e.Key == Key.Escape)
            {
                e.Handled = true;
                CloseOrCancel();
            }
            else if (e.Key == Key.Enter && !vm.IsBusy)
            {
                e.Handled = true;
                _ = PullAsync();
            }
        };

        Content = new StackPanel().Vertical().Spacing(10).Padding(20).Children(
            FormParts.Field("Image", reference),
            FormParts.Hint("Images come from Docker Hub unless the name includes a registry. Progress details appear when the pull finishes."),
            progress,
            FormParts.BoundText(vm, nameof(vm.Status), x => x.Status),
            FormParts.BoundText(vm, nameof(vm.Error), x => x.Error, FormParts.ErrorColor),
            new StackPanel().Horizontal().Spacing(8).Right().Children(pullButton, closeButton));

        Loaded += () => FocusManager.SetFocus(reference);
    }

    /// <summary>The image that was pulled, if any.</summary>
    public string? PulledReference { get; private set; }

    private async Task PullAsync()
    {
        if (await _vm.PullAsync())
        {
            PulledReference = _vm.Reference.Trim();
            Close();
        }
    }

    private void CloseOrCancel()
    {
        if (_vm.IsBusy)
        {
            _vm.Cancel();
        }
        else
        {
            Close();
        }
    }
}
