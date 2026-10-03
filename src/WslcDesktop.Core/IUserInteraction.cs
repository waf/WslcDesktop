namespace WslcDesktop.Core;

/// <summary>UI services view models need but can't implement themselves (dialogs, toasts). Implemented by the app.</summary>
public interface IUserInteraction
{
    /// <summary>A short, non-blocking notification.</summary>
    void ShowToast(string message);

    void ShowError(string title, Exception exception);

    Task<bool> ConfirmAsync(string title, string message, string confirmText);
}
