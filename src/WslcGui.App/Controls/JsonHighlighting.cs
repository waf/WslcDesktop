using Aprillz.MewUI;
using Aprillz.MewUI.Controls;
using Aprillz.MewUI.Text;

using WslcGui.Core;

namespace WslcGui.App.Controls;

/// <summary>Colors JSON in a text view through MewUI's classifier pipeline (no editor control needed).</summary>
internal sealed class JsonClassifier : ITextClassifier
{
    // VS Code-like palettes for light and dark backgrounds.
    private static readonly Color[] s_light = [Color.FromRgb(4, 81, 165), Color.FromRgb(163, 21, 21), Color.FromRgb(9, 134, 88), Color.FromRgb(0, 0, 255), Color.FromRgb(120, 120, 120)];
    private static readonly Color[] s_dark = [Color.FromRgb(156, 220, 254), Color.FromRgb(206, 145, 120), Color.FromRgb(181, 206, 168), Color.FromRgb(86, 156, 214), Color.FromRgb(150, 150, 150)];

    public void Classify(in TextClassificationContext context, IList<TextPaintSpan> output)
    {
        var palette = Application.IsRunning && Application.Current.Theme.IsDark ? s_dark : s_light;
        foreach (var token in JsonTokenizer.TokenizeLine(context.Text.Span))
        {
            output.Add(new TextPaintSpan(new TextRange(token.Start, token.Length), Foreground: palette[(int)token.Kind]));
        }
    }

    /// <summary>Adds JSON coloring to a text box and keeps it right when the theme changes.</summary>
    public static MultiLineTextBox Attach(MultiLineTextBox view)
    {
        view.Extensions.Classifiers.Add(new JsonClassifier());
        if (Application.IsRunning)
        {
            Application.Current.ThemeChanged += (_, _) => view.InvalidateTextView();
        }

        return view;
    }
}
