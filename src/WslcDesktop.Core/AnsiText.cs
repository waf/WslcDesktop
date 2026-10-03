using System.Text.RegularExpressions;

namespace WslcDesktop.Core;

public static partial class AnsiText
{
    /// <summary>Removes ANSI/VT escape sequences (colors, cursor movement, titles) from container output.</summary>
    public static string Strip(string text) =>
        text.Contains('\x1b', StringComparison.Ordinal) ? EscapeRegex().Replace(text, string.Empty) : text;

    // CSI (ESC [ ... final), OSC (ESC ] ... BEL or ESC \), and two-character escapes (ESC x).
    [GeneratedRegex(@"\x1b(?:\[[0-?]*[ -/]*[@-~]|\][^\x07\x1b]*(?:\x07|\x1b\\)|[@-Z\\-_])")]
    private static partial Regex EscapeRegex();
}
