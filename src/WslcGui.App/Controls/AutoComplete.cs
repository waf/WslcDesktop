using Aprillz.MewUI;
using Aprillz.MewUI.Controls;
using Aprillz.MewUI.Input;

using WslcGui.Core;

namespace WslcGui.App.Controls;

/// <summary>
/// Adds a suggestion list under a <see cref="TextBox"/>. Suggestions only help: any text can still be typed.
/// Keys: Down/Up to move, Enter or Tab to accept, Escape to dismiss, Down on an empty box to list everything.
/// </summary>
#pragma warning disable CA1001 // The popup lives exactly as long as the text box it is attached to.
internal sealed class AutoComplete
#pragma warning restore CA1001
{
    private const double RowHeight = 26;

    private static AutoComplete? s_open;

    private readonly TextBox _box;
    private readonly Func<string, IReadOnlyList<string>> _suggest;
    private readonly Popup _popup = new() { StaysOpen = false };
    private readonly StackPanel _rows = new StackPanel().Vertical();
    private IReadOnlyList<string> _items = [];
    private int _selected = -1;
    private bool _settingText;

    private AutoComplete(TextBox box, Func<string, IReadOnlyList<string>> suggest)
    {
        _box = box;
        _suggest = suggest;
        _popup.Content = new Border()
            .BorderThickness(1)
            .CornerRadius(4)
            .Padding(2)
            .WithTheme((theme, border) => border.Background(theme.Palette.ControlBackground).BorderBrush(theme.Palette.ControlBorder))
            .Child(_rows);
        _popup.Closed += (_, _) =>
        {
            if (ReferenceEquals(s_open, this))
            {
                s_open = null;
            }
        };

        box.OnTextChanged(text =>
        {
            if (!_settingText && box.IsFocused)
            {
                Show(text);
            }
        });
        box.KeyDown += OnKeyDown;
        box.LostFocus += () => Application.Current.Dispatcher?.BeginInvoke(DispatcherPriority.Background, () =>
        {
            // Let a click on a suggestion land before closing.
            if (!_box.IsFocused)
            {
                Close();
            }
        });
    }

    /// <summary>
    /// Whether any suggestion list is showing. Dialogs check this in their own Enter/Escape handling so those keys
    /// go to the list first.
    /// </summary>
    public static bool IsAnyOpen => s_open is not null;

    /// <param name="suggest">Returns suggestions for the current text (called on every edit; keep it cheap).</param>
    public static TextBox Attach(TextBox box, Func<string, IReadOnlyList<string>> suggest)
    {
        _ = new AutoComplete(box, suggest);
        return box;
    }

    /// <summary>Suggestions from a fixed or changing list of candidates.</summary>
    public static TextBox Attach(TextBox box, Func<IEnumerable<string>> candidates) =>
        Attach(box, text => Suggestions.Filter(candidates(), text));

    private void OnKeyDown(KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.Down when !_popup.IsOpen:
                Show(_box.Text);
                e.Handled = _popup.IsOpen;
                break;
            case Key.Down when _items.Count > 0:
                Select((_selected + 1) % _items.Count);
                e.Handled = true;
                break;
            case Key.Up when _popup.IsOpen && _items.Count > 0:
                Select(_selected <= 0 ? _items.Count - 1 : _selected - 1);
                e.Handled = true;
                break;
            case Key.Enter or Key.Tab when _popup.IsOpen && _selected >= 0:
                Accept(_items[_selected]);
                e.Handled = true;
                break;
            case Key.Escape when _popup.IsOpen:
                Close();
                e.Handled = true;
                break;
        }
    }

    private void Show(string text)
    {
        _items = _suggest(text);
        if (_items.Count == 0 || _box.FindVisualRoot() is not Window)
        {
            Close();
            return;
        }

        _rows.Clear();
        for (var i = 0; i < _items.Count; i++)
        {
            var index = i;
            var row = new Border()
                .Padding(8, 0)
                .Height(RowHeight)
                .CornerRadius(3)
                .Child(new TextBlock().Text(_items[i]).CenterVertical().TextTrimming(TextTrimming.CharacterEllipsis));
            row.MouseDown += e =>
            {
                Accept(_items[index]);
                e.Handled = true;
            };
            _rows.Add(row);
        }

        _selected = -1;
        UpdateHighlight();
        _rows.Width(Math.Max(120, _box.Bounds.Width - 6));
        _popup.ShowAt(_box, _box.Bounds, PopupAnchorSide.Below);
        s_open = this;
    }

    private void Select(int index)
    {
        _selected = index;
        UpdateHighlight();
    }

    private void UpdateHighlight()
    {
        if (!Application.IsRunning)
        {
            return;
        }

        var palette = Application.Current.Theme.Palette;
        for (var i = 0; i < _rows.Children.Count; i++)
        {
            ((Border)_rows.Children[i]).Background(i == _selected ? palette.SelectionBackground : Color.Transparent);
        }
    }

    private void Accept(string value)
    {
        _settingText = true;
        try
        {
            _box.Text(value);
            _box.MoveCaret(value.Length, extendSelection: false);
        }
        finally
        {
            _settingText = false;
        }

        Close();
        _box.Focus();
    }

    private void Close()
    {
        _popup.Close();
        if (ReferenceEquals(s_open, this))
        {
            s_open = null;
        }
    }
}
