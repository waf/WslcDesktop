using System.ComponentModel;

using Aprillz.MewUI;
using Aprillz.MewUI.Controls;

using WslcDesktop.App.Icons;
using WslcDesktop.Core;

namespace WslcDesktop.App.Pages;

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
    /// <summary>
    /// Horizontal inset of page content from the window/pane edges. Headers, toolbars and grids all use it so their
    /// left and right edges line up.
    /// </summary>
    public const double Inset = 16;

    public static DockPanel Header(string title, params FrameworkElement[] actions) =>
        new DockPanel()
            .Padding(Inset, 16, Inset, 12)
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

    /// <summary>
    /// "Starting engine", "engine idle" and refresh errors, shown next to the search box on one trimmed line (full text in
    /// the tooltip). It sits in a row whose height doesn't depend on it, so it never moves the grid when it changes.
    /// </summary>
    public static TextBlock StatusLine<TRow>(ResourceListViewModel<TRow> vm)
        where TRow : class
    {
        var line = new TextBlock().TextTrimming(TextTrimming.CharacterEllipsis).CenterVertical().Margin(8, 0);
        void Update()
        {
            var text = vm switch
            {
                { ErrorText: { } error } => $"⚠ {error}",
                { IsStartingEngine: true } => "Starting the WSLC engine… (this takes a few seconds after it has been idle)",
                { IsEngineIdle: true } => "Engine idle (nothing running): showing the last known state",
                _ => string.Empty,
            };
            line.Text = text;
            line.ToolTip(text.Length > 0 ? text : null);
        }

        vm.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName is nameof(vm.ErrorText) or nameof(vm.IsStartingEngine) or nameof(vm.IsEngineIdle))
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
        var hint = new TextBlock().Text(text).Center().WithTheme((theme, t) => t.Foreground(theme.Palette.WindowText.WithAlpha(170)));
        // Not while a refresh error is showing: then the list is empty because the engine couldn't be read.
        void Update() => hint.IsVisible = vm.Items.Count == 0 && !vm.IsLoading && vm.ErrorText is null;
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
