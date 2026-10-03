using Aprillz.MewUI;
using Aprillz.MewUI.Controls;
using Aprillz.MewUI.Input;

using WslcGui.App.Pages;
using WslcGui.Core;

namespace WslcGui.App.Dialogs;

/// <summary>Asks for one line of text, optionally with a checkbox.</summary>
internal sealed class PromptDialog : Window
{
    private readonly TextBox _input;
    private readonly CheckBox? _option;

    private PromptDialog(string title, string label, string placeholder, string confirmText, string? optionText)
    {
        Title = title;
        StartupLocation = WindowStartupLocation.CenterOwner;
        WindowSize = WindowSize.FitContentHeight(460, 300);
        _input = new TextBox().Placeholder(placeholder);
        _option = optionText is null ? null : new CheckBox().Content(optionText);

        PreviewKeyDown += e =>
        {
            if (e.Key == Key.Escape)
            {
                e.Handled = true;
                Close();
            }
            else if (e.Key == Key.Enter)
            {
                e.Handled = true;
                Confirm();
            }
        };

        var panel = new StackPanel().Vertical().Spacing(10).Padding(20).Children(FormParts.Field(label, _input));
        if (_option is not null)
        {
            panel.Add(_option);
        }

        panel.Add(new StackPanel().Horizontal().Spacing(8).Right().Children(
            new Button().MinWidth(96).Content(confirmText).OnClick(Confirm),
            new Button().MinWidth(96).Content("Cancel").OnClick(Close)));
        Content = panel;
        Loaded += () => FocusManager.SetFocus(_input);
    }

    private (string Text, bool Option)? Result { get; set; }

    /// <returns>The trimmed text and checkbox state, or null if cancelled or empty.</returns>
    public static async Task<(string Text, bool Option)?> ShowAsync(Window? owner, string title, string label, string placeholder, string confirmText, string? optionText = null)
    {
        var dialog = new PromptDialog(title, label, placeholder, confirmText, optionText);
        await dialog.ShowDialogAsync(owner);
        return dialog.Result;
    }

    private void Confirm()
    {
        var text = _input.Text.Trim();
        if (text.Length > 0)
        {
            Result = (text, _option?.IsChecked == true);
            Close();
        }
    }
}

/// <summary>Shows read-only text (inspect JSON) with copy.</summary>
internal sealed class TextViewerDialog : Window
{
    public TextViewerDialog(string title, string text)
    {
        Title = title;
        StartupLocation = WindowStartupLocation.CenterOwner;
        WindowSize = WindowSize.Resizable(760, 640);
        var view = new MultiLineTextBox().IsReadOnly().Wrap(false).FontFamily("Cascadia Mono").Text(text);
        PreviewKeyDown += e =>
        {
            if (e.Key == Key.Escape)
            {
                e.Handled = true;
                Close();
            }
        };

        Content = new DockPanel().Padding(16).Children(
            new StackPanel().DockBottom().Horizontal().Spacing(8).Right().Margin(0, 12, 0, 0).Children(
                new Button().MinWidth(96).Content("Copy").OnClick(() => PageParts.CopyToClipboard(text)),
                new Button().MinWidth(96).Content("Close").OnClick(Close)),
            view);
    }
}

/// <summary>Live output of a long operation (push), with cancel.</summary>
internal sealed class CommandOutputDialog : Window
{
    private readonly CommandOutputViewModel _vm;

    public CommandOutputDialog(string title, CommandOutputViewModel vm)
    {
        _vm = vm;
        Title = title;
        StartupLocation = WindowStartupLocation.CenterOwner;
        WindowSize = WindowSize.Resizable(760, 520);
        Content = Build(vm, CloseOrCancel);
    }

    /// <summary>The output area shared with the build dialog: status line, progress bar and the log.</summary>
    public static DockPanel BuildOutputArea(CommandOutputViewModel vm)
    {
        var log = new MultiLineTextBox().IsReadOnly().Wrap(false).FontFamily("Cascadia Mono");
        log.Document.UndoSizeLimit = 0;
        vm.LineAdded += line => log.AppendText(line + "\n", scrollToCaret: true);

        var progress = new ProgressBar { IsIndeterminate = true }.IsVisible(false);
        var status = new TextBlock().TextWrapping(TextWrapping.Wrap);
        vm.PropertyChanged += (_, e) =>
        {
            switch (e.PropertyName)
            {
                case nameof(vm.IsBusy):
                    progress.IsVisible = vm.IsBusy;
                    break;
                case nameof(vm.Status):
                case nameof(vm.Succeeded):
                    status.Text = vm.Status ?? string.Empty;
                    if (vm.Succeeded == false)
                    {
                        status.Foreground(FormParts.ErrorColor);
                    }

                    break;
            }
        };

        return new DockPanel().Children(
            new StackPanel().DockTop().Vertical().Spacing(6).Margin(0, 0, 0, 8).Children(status, progress),
            log);
    }

    private static DockPanel Build(CommandOutputViewModel vm, Action closeOrCancel)
    {
        var close = new Button().MinWidth(96).Content("Cancel").OnClick(closeOrCancel);
        vm.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(vm.IsBusy))
            {
                close.Content(vm.IsBusy ? "Cancel" : "Close");
            }
        };

        return new DockPanel().Padding(16).Children(
            new StackPanel().DockBottom().Horizontal().Right().Margin(0, 12, 0, 0).Children(close),
            BuildOutputArea(vm));
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

/// <summary>Registry sign-in. The password goes to wslc on stdin, never on a command line.</summary>
internal sealed class RegistryLoginDialog : Window
{
    private readonly RegistryLoginViewModel _vm;

    public RegistryLoginDialog(RegistryLoginViewModel vm)
    {
        _vm = vm;
        Title = "Sign in to a registry";
        StartupLocation = WindowStartupLocation.CenterOwner;
        WindowSize = WindowSize.FitContentSize(480, 380);

        var server = FormParts.Input(vm.Server, "Leave empty for Docker Hub, or e.g. ghcr.io", text => vm.Server = text);
        var password = new PasswordBox();
        password.PasswordChanged += () => vm.Password = password.Password;
        var signIn = new Button().MinWidth(96).Content("Sign in").OnClick(() => _ = SignInAsync());
        vm.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(vm.IsBusy))
            {
                signIn.IsEnabled = !vm.IsBusy;
            }
        };

        PreviewKeyDown += e =>
        {
            if (e.Key == Key.Escape)
            {
                e.Handled = true;
                Close();
            }
            else if (e.Key == Key.Enter)
            {
                e.Handled = true;
                _ = SignInAsync();
            }
        };

        Content = new StackPanel().Vertical().Spacing(10).Padding(20).Children(
            FormParts.Field("Registry", server),
            FormParts.Field("Username", FormParts.Input(vm.Username, string.Empty, text => vm.Username = text)),
            FormParts.Field("Password or access token", password),
            FormParts.BoundText(vm, nameof(vm.Error), x => x.Error, FormParts.ErrorColor),
            new StackPanel().Horizontal().Spacing(8).Right().Children(
                signIn,
                new Button().MinWidth(96).Content("Cancel").OnClick(Close)));
        Loaded += () => FocusManager.SetFocus(server);
    }

    public bool SignedIn { get; private set; }

    private async Task SignInAsync()
    {
        if (await _vm.LoginAsync())
        {
            SignedIn = true;
            Close();
        }
    }
}
