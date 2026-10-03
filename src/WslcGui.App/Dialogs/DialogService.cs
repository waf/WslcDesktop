using Aprillz.MewUI;
using Aprillz.MewUI.Controls;

using WslcGui.Core;
using WslcGui.Engine;

namespace WslcGui.App.Dialogs;

/// <summary>Opens the app's dialogs with the engine services they need.</summary>
internal sealed class DialogService(
    IContainerLifecycle lifecycle,
    IImageService images,
    Func<RunSpec, string>? describeRun,
    IUserInteraction ui,
    Func<Window?> owner)
{
    /// <summary>Raised after a container was started from the Run dialog.</summary>
    public event Action? ContainerStarted;

    /// <summary>Raised after an image was pulled.</summary>
    public event Action? ImagePulled;

    public async Task ShowRunAsync(string image = "")
    {
        var vm = new RunContainerViewModel(lifecycle, image, describeRun);
        var dialog = new RunContainerDialog(vm);
        await dialog.ShowDialogAsync(owner());
        if (dialog.ContainerId is { } id)
        {
            ui.ShowToast($"Started {(string.IsNullOrWhiteSpace(vm.Name) ? Formatting.ShortId(id) : vm.Name.Trim())}.");
            ContainerStarted?.Invoke();
        }
    }

    public async Task ShowPullAsync()
    {
        using var vm = new PullImageViewModel(images);
        var dialog = new PullImageDialog(vm);
        await dialog.ShowDialogAsync(owner());
        if (dialog.PulledReference is { } reference)
        {
            ui.ShowToast($"Pulled {reference}.");
            ImagePulled?.Invoke();
        }
    }
}
