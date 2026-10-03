using System.ComponentModel;

using Aprillz.MewUI;
using Aprillz.MewUI.Controls;

using WslcGui.App.Icons;
using WslcGui.Core;

namespace WslcGui.App.Pages;

/// <summary>A page that refreshes itself while it's shown.</summary>
internal interface IRefreshablePage
{
    Task RefreshAsync(RefreshReason reason);

    /// <summary>Called when the page is shown (true) or hidden (false).</summary>
    void SetActive(bool active)
    {
    }
}

/// <summary>Building blocks shared by the list pages.</summary>
internal static class PageParts
{
    public static DockPanel Header(string title, params FrameworkElement[] actions) =>
        new DockPanel()
            .Padding(24, 16, 24, 12)
            .Spacing(12)
            .Children(
                new StackPanel().DockRight().Horizontal().Spacing(4).Children(actions),
                new TextBlock().Text(title).FontSize(ThemeFontSize.Medium).SemiBold().CenterVertical());

    public static Button ToolButton(string icon, string text, Action onClick) =>
        new Button()
            .StyleName("flat-button")
            .Padding(10, 6)
            .Content(new StackPanel().Horizontal().Spacing(7).Children(
                IconFactory.Create(icon).CenterVertical(),
                new TextBlock().Text(text).CenterVertical()))
            .OnClick(onClick);

    public static TextBox SearchBox(string placeholder, Action<string> onChanged) =>
        new TextBox()
            .Width(240)
            .Placeholder(placeholder)
            .CenterVertical()
            .OnTextChanged(onChanged);

    /// <summary>A line above the grid for "starting engine", "engine idle" and refresh errors.</summary>
    public static TextBlock StatusLine<TRow>(ResourceListViewModel<TRow> vm)
        where TRow : class
    {
        var line = new TextBlock().Margin(24, 0, 24, 8).TextWrapping(TextWrapping.Wrap);
        void Update()
        {
            var (text, visible) = vm switch
            {
                { ErrorText: { } error } => ($"⚠ {error}", true),
                { IsLoading: true } => ("Starting the WSLC engine… (the first request after it has been idle takes a few seconds)", true),
                { IsEngineIdle: true } => ("The WSLC engine is idle (no running containers). Showing the last known state; it starts again on demand.", true),
                _ => (string.Empty, false),
            };
            line.Text = text;
            line.IsVisible = visible;
        }

        vm.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName is nameof(vm.ErrorText) or nameof(vm.IsLoading) or nameof(vm.IsEngineIdle))
            {
                Update();
            }
        };
        Update();
        return line;
    }

    /// <summary>Text shown over an empty grid.</summary>
    public static TextBlock EmptyHint<TRow>(ResourceListViewModel<TRow> vm, string text)
        where TRow : class
    {
        var hint = new TextBlock().Text(text).Center().WithTheme((theme, t) => t.Foreground(theme.Palette.PlaceholderText));
        void Update() => hint.IsVisible = vm.Items.Count == 0 && !vm.IsLoading;
        vm.Items.CollectionChanged += (_, _) => Update();
        ((INotifyPropertyChanged)vm).PropertyChanged += (_, _) => Update();
        Update();
        return hint;
    }

    public static GridViewColumn<T> TextColumn<T, TKey>(string header, double width, Func<T, string> text, Func<T, TKey> sortKey, bool alignRight = false) =>
        new GridViewColumn<T>()
            .Header(header)
            .Width(width)
            .SortBy(sortKey)
            .Bind(
                _ => new TextBlock { TextAlignment = alignRight ? TextAlignment.Right : TextAlignment.Left }.Margin(8, 0).CenterVertical(),
                (view, item) => view.Text(text(item)));

    public static GridViewColumn<T> StarTextColumn<T, TKey>(string header, double stars, double minWidth, Func<T, string> text, Func<T, TKey> sortKey) =>
        new GridViewColumn<T>()
            .Header(header)
            .StarWidth(stars, minWidth: minWidth)
            .SortBy(sortKey)
            .Bind(
                _ => new TextBlock().Margin(8, 0).CenterVertical(),
                (view, item) => view.Text(text(item)));

    /// <summary>The selected rows of a grid, falling back to the row a context menu was opened on.</summary>
    public static IReadOnlyList<T> Targets<T>(GridView grid, T? clicked = null)
        where T : class
    {
        var selected = grid.SelectedItems.OfType<T>().ToList();
        if (clicked is null)
        {
            return selected;
        }

        return selected.Contains(clicked) ? selected : [clicked];
    }

    public static void CopyToClipboard(string text)
    {
        if (Application.IsRunning)
        {
            Application.Current.PlatformServices.Clipboard?.TrySetText(text);
        }
    }
}
