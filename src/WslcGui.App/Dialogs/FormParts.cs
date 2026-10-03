using System.Collections.ObjectModel;
using System.ComponentModel;

using Aprillz.MewUI;
using Aprillz.MewUI.Controls;

using WslcGui.App.Icons;

namespace WslcGui.App.Dialogs;

internal static class FormParts
{
    public static readonly Color ErrorColor = Color.FromRgb(196, 43, 28);

    public static TextBlock SectionTitle(string text) => new TextBlock().Text(text).SemiBold().Margin(0, 8, 0, 0);

    public static TextBlock Hint(string text) =>
        new TextBlock().Text(text).TextWrapping(TextWrapping.Wrap).WithTheme((theme, t) => t.Foreground(theme.Palette.WindowText.WithAlpha(170)));

    /// <summary>A label above an input.</summary>
    public static StackPanel Field(string label, FrameworkElement input) =>
        new StackPanel().Vertical().Spacing(4).Children(new TextBlock().Text(label), input);

    public static TextBox Input(string text, string placeholder, Action<string> onChanged) =>
        new TextBox().Text(text).Placeholder(placeholder).OnTextChanged(onChanged);

    /// <summary>A text block that shows a view model string property, hidden while it's empty.</summary>
    public static TextBlock BoundText<TSource>(TSource source, string propertyName, Func<TSource, string?> read, Color? color = null)
        where TSource : INotifyPropertyChanged
    {
        var text = new TextBlock().TextWrapping(TextWrapping.Wrap);
        if (color is { } c)
        {
            text.Foreground(c);
        }

        void Update()
        {
            var value = read(source);
            text.Text = value ?? string.Empty;
            text.IsVisible = !string.IsNullOrEmpty(value);
        }

        source.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == propertyName)
            {
                Update();
            }
        };
        Update();
        return text;
    }

    /// <summary>
    /// An editable list of rows ("+ Add" button, one line per item with a remove button) kept in sync with
    /// <paramref name="items"/>.
    /// </summary>
    public static StackPanel RowList<T>(
        string title,
        string addText,
        ObservableCollection<T> items,
        Func<T> create,
        Func<T, FrameworkElement> buildRow)
    {
        var rows = new StackPanel().Vertical().Spacing(6);
        void Rebuild()
        {
            rows.Clear();
            foreach (var item in items)
            {
                var remove = new Button()
                    .StyleName("flat-button")
                    .Padding(6, 4)
                    .ToolTip("Remove")
                    .Content(IconFactory.Create(IconData.Delete, 14))
                    .OnClick(() => items.Remove(item));
                rows.Add(new DockPanel().Spacing(6).Children(remove.DockRight(), buildRow(item)));
            }
        }

        items.CollectionChanged += (_, _) => Rebuild();
        Rebuild();

        var add = new Button()
            .StyleName("flat-button")
            .Padding(6, 4)
            .Left()
            .Content(new StackPanel().Horizontal().Spacing(6).Children(
                IconFactory.Create(IconData.Add, 14).CenterVertical(),
                new TextBlock().Text(addText).CenterVertical()))
            .OnClick(() => items.Add(create()));

        return new StackPanel().Vertical().Spacing(6).Children(SectionTitle(title), rows, add);
    }
}
