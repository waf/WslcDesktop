using Aprillz.MewUI;
using Aprillz.MewUI.Controls;

using WslcDesktop.Core;
using WslcDesktop.Engine;

namespace WslcDesktop.App.Shell;

/// <summary>MewUI implementation of the dialogs and notifications view models ask for.</summary>
internal sealed class UserInteraction(Func<Window?> owner) : IUserInteraction
{
    public void ShowToast(string message) => owner()?.ShowToast(message);

    public void ShowError(string title, Exception exception)
    {
        var detail = exception is EngineException { Detail: { } engineDetail } ? engineDetail : exception.ToString();
        _ = MessageBox.PromptAsync(new MessageBoxOptions
        {
            Title = title,
            Message = $"{title}.\n\n{exception.Message}",
            Detail = detail,
            Icon = PromptIconKind.Error,
            Owner = owner(),
            Buttons = [new MessageButton("OK", MessageButtonRole.Accept)],
        });
    }

    public async Task<bool> ConfirmAsync(string title, string message, string confirmText)
    {
        var result = await MessageBox.PromptAsync(new MessageBoxOptions
        {
            Title = title,
            Message = message,
            Icon = PromptIconKind.Warning,
            Owner = owner(),
            // MewUI roles: Accept => true, Reject => false, Destructive => null ("No"). The confirm button is the Accept one.
            Buttons = [new MessageButton(confirmText, MessageButtonRole.Accept), new MessageButton("Cancel", MessageButtonRole.Reject)],
        });
        return result == true;
    }
}
