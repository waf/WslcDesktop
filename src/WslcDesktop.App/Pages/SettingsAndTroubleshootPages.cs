using System.ComponentModel;

using Aprillz.MewUI;
using Aprillz.MewUI.Controls;

using WslcDesktop.App.Dialogs;
using WslcDesktop.App.Icons;
using WslcDesktop.App.Shell;
using WslcDesktop.Core;

namespace WslcDesktop.App.Pages;

internal sealed class SettingsPage : UserControl
{
    private readonly Func<AppSettings> _getSettings;
    private readonly Action<AppSettings> _setSettings;
    private readonly TroubleshootViewModel _maintenance;
    private readonly EngineStatusViewModel _engineStatus;

    public SettingsPage(Func<AppSettings> getSettings, Action<AppSettings> setSettings, TroubleshootViewModel maintenance, EngineStatusViewModel engineStatus)
    {
        _getSettings = getSettings;
        _setSettings = setSettings;
        _maintenance = maintenance;
        _engineStatus = engineStatus;
        Build();
    }

    protected override Element OnBuild()
    {
        var settings = _getSettings();
        RadioButton ThemeRadio(string text, AppTheme theme) => new RadioButton()
            .Content(text)
            .GroupName("theme")
            .IsChecked(settings.Theme == theme)
            .OnChecked(() => _setSettings(_getSettings() with { Theme = theme }));

        var version = new TextBlock();
        void UpdateVersion() => version.Text = $"WSLC Desktop {typeof(SettingsPage).Assembly.GetName().Version?.ToString(3)}   ·   WSLC {_engineStatus.Version ?? "unknown"}";
        ((INotifyPropertyChanged)_engineStatus).PropertyChanged += (_, _) => UpdateVersion();
        UpdateVersion();

        return new ScrollViewer().AutoVerticalScroll().NoHorizontalScroll().Content(
            new StackPanel().Vertical().Spacing(24).Padding(PageParts.Inset, 16).Children(
                new TextBlock().Text("Settings").FontSize(ThemeFontSize.Medium).SemiBold(),
                Section(
                    "Appearance",
                    "Choose the color mode.",
                    new StackPanel().Horizontal().Spacing(18).Children(
                        ThemeRadio("Use system setting", AppTheme.System),
                        ThemeRadio("Light", AppTheme.Light),
                        ThemeRadio("Dark", AppTheme.Dark))),
                Section(
                    "Window",
                    "With this on, closing the window keeps WSLC Desktop in the notification area. Use the tray icon's Quit to exit.",
                    new CheckBox()
                        .Content("Keep running in the notification area when the window is closed")
                        .IsChecked(settings.CloseToTray)
                        .OnCheckedChanged(isChecked => _setSettings(_getSettings() with { CloseToTray = isChecked }))),
                Section(
                    "Engine",
                    "WSLC's own settings (resources, idle timeout, registries and more) live in a YAML file that wslc opens in your editor. Changes apply the next time the engine starts.",
                    new Button().Content("Open WSLC settings file").Padding(12, 6).Left().OnClick(_maintenance.OpenEngineSettings)),
                Section("About", "A desktop app for WSL containers, built on the wslc command line.", version)));
    }

    private static StackPanel Section(string title, string description, FrameworkElement content) =>
        new StackPanel().Vertical().Spacing(8).Children(
            new TextBlock().Text(title).SemiBold(),
            FormParts.Hint(description),
            content);
}

internal sealed class TroubleshootPage : UserControl, IRefreshablePage
{
    private readonly TroubleshootViewModel _vm;

    public TroubleshootPage(TroubleshootViewModel vm)
    {
        _vm = vm;
        Build();
    }

    public Task RefreshAsync(RefreshReason reason) => reason == RefreshReason.User ? _vm.RefreshAsync() : Task.CompletedTask;

    protected override Element OnBuild()
    {
        var details = new ObservableCollectionView<KeyValuePair<string, string>>();
        var detailsGrid = new GridView { ItemsSource = ItemsView.Create(details.Items, kv => kv.Key, kv => kv.Key), ZebraStriping = true, ShowGridLines = false };
        detailsGrid.Columns(
            PageParts.TextColumn<KeyValuePair<string, string>, string>("Item", 260, kv => kv.Key, kv => kv.Key),
            PageParts.StarTextColumn<KeyValuePair<string, string>, string>("Value", 1, 200, kv => kv.Value, kv => kv.Value));
        _vm.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(_vm.Details))
            {
                details.Replace(_vm.Details);
            }
        };

        var commandsGrid = new GridView { ItemsSource = ItemsView.Create(_vm.RecentCommands), ZebraStriping = true, ShowGridLines = false };
        commandsGrid.Columns(
            PageParts.TextColumn<CommandLogEntry, DateTimeOffset>("Time", 80, c => c.TimeText, c => c.Time),
            PageParts.TextColumn<CommandLogEntry, TimeSpan>("Duration", 80, c => c.DurationText, c => c.Duration, alignRight: true),
            PageParts.TextColumn<CommandLogEntry, string>("Result", 160, c => c.ResultText, c => c.ResultText),
            PageParts.StarTextColumn<CommandLogEntry, string>("Command", 1, 300, c => c.Command, c => c.Command));

        return new Grid()
            .Rows("Auto, Auto, Auto, 2*, Auto, 3*")
            .Children(
                PageParts.Header(
                    "Troubleshoot",
                    PageParts.ToolButton(IconData.ArrowClockwise, "Refresh", () => _ = _vm.RefreshAsync()),
                    PageParts.ToolButton(IconData.Copy, "Copy diagnostics", () => PageParts.CopyToClipboard(_vm.GetDiagnosticsText())),
                    PageParts.ToolButton(IconData.Settings, "WSLC settings file", _vm.OpenEngineSettings),
                    PageParts.ToolButton(IconData.Wrench, "Restart engine", () => _ = _vm.RestartEngineAsync())),
                FormParts.BoundText(_vm, nameof(_vm.DetailsError), x => x.DetailsError, FormParts.ErrorColor).Row(1).Margin(PageParts.Inset, 0, PageParts.Inset, 8),
                new TextBlock().Text("Engine").SemiBold().Row(2).Margin(PageParts.Inset, 0, PageParts.Inset, 6),
                detailsGrid.Row(3).Margin(PageParts.Inset, 0, PageParts.Inset, 12),
                new TextBlock().Text("Recent wslc commands").SemiBold().Row(4).Margin(PageParts.Inset, 0, PageParts.Inset, 6),
                commandsGrid.Row(5).Margin(PageParts.Inset, 0, PageParts.Inset, 16));
    }

    /// <summary>Holds a replaceable list for a grid.</summary>
    private sealed class ObservableCollectionView<T>
    {
        public System.Collections.ObjectModel.ObservableCollection<T> Items { get; } = [];

        public void Replace(IEnumerable<T> items)
        {
            Items.Clear();
            foreach (var item in items)
            {
                Items.Add(item);
            }
        }
    }
}
