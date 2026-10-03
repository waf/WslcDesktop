using Aprillz.MewUI;
using Aprillz.MewUI.Controls;

using WslcGui.Core;
using WslcGui.Engine;

namespace WslcGui.App.Dialogs;

/// <summary>Opens the app's dialogs with the engine services they need.</summary>
internal sealed class DialogService(
    IContainerLifecycle lifecycle,
    IImageService images,
    IRegistryService registry,
    Func<RunSpec, string>? describeRun,
    IUserInteraction ui,
    Func<Window?> owner)
{
    public Window? Owner => owner();

    /// <summary>Raised after an image was built.</summary>
    public event Action? ImageBuilt;

    public async Task ShowBuildAsync()
    {
        using var vm = new BuildImageViewModel(images);
        var dialog = new BuildImageDialog(vm);
        await dialog.ShowDialogAsync(owner());
        if (dialog.Built)
        {
            ImageBuilt?.Invoke();
        }
    }

    public async Task ShowPushAsync(string reference)
    {
        using var output = new CommandOutputViewModel();
        var dialog = new CommandOutputDialog($"Push {reference}", output);
        var push = output.RunAsync($"Pushing {reference}… (output appears when the push finishes)", $"Pushed {reference}.", (progress, ct) => images.PushAsync(reference, progress, ct));
        await dialog.ShowDialogAsync(owner());
        await push;
    }

    public async Task ShowRegistryLoginAsync()
    {
        var dialog = new RegistryLoginDialog(new RegistryLoginViewModel(registry));
        await dialog.ShowDialogAsync(owner());
        if (dialog.SignedIn)
        {
            ui.ShowToast("Signed in.");
        }
    }

    public Task ShowTextAsync(string title, string text) => new TextViewerDialog(title, text).ShowDialogAsync(owner());

    public Task<(string Text, bool Option)?> PromptAsync(string title, string label, string placeholder, string confirmText, string? optionText = null) =>
        PromptDialog.ShowAsync(owner(), title, label, placeholder, confirmText, optionText);

    public async Task<string?> PickFolderAsync(string title) =>
        await FileDialog.SelectFolderAsync(new FolderDialogOptions { Owner = owner(), Title = title });

    public async Task<string?> PickFileAsync(string title) =>
        await FileDialog.OpenFileAsync(new OpenFileDialogOptions { Owner = owner(), Title = title });

    public async Task<string?> PickSaveFileAsync(string title, string fileName) =>
        await FileDialog.SaveFileAsync(new SaveFileDialogOptions { Owner = owner(), Title = title, FileName = fileName });

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
