using System.Text;

namespace WslcGui.Core;

/// <summary>
/// Splits a command typed by the user into arguments using POSIX shell quoting rules (the command runs in a
/// Linux container): whitespace separates, '...' is literal, "..." allows \" and \\, and a backslash escapes outside quotes.
/// </summary>
public static class CommandLineSplitter
{
    /// <returns>The arguments, or null with <paramref name="error"/> set if quoting is unbalanced.</returns>
    public static IReadOnlyList<string>? TrySplit(string text, out string? error)
    {
        var arguments = new List<string>();
        var current = new StringBuilder();
        var inArgument = false;
        char? quote = null;

        for (var i = 0; i < text.Length; i++)
        {
            var c = text[i];
            if (quote == '\'')
            {
                if (c == '\'')
                {
                    quote = null;
                }
                else
                {
                    current.Append(c);
                }
            }
            else if (quote == '"')
            {
                if (c == '"')
                {
                    quote = null;
                }
                else if (c == '\\' && i + 1 < text.Length && text[i + 1] is '"' or '\\' or '$' or '`')
                {
                    current.Append(text[++i]);
                }
                else
                {
                    current.Append(c);
                }
            }
            else if (char.IsWhiteSpace(c))
            {
                if (inArgument)
                {
                    arguments.Add(current.ToString());
                    current.Clear();
                    inArgument = false;
                }
            }
            else
            {
                inArgument = true;
                if (c is '\'' or '"')
                {
                    quote = c;
                }
                else if (c == '\\' && i + 1 < text.Length)
                {
                    current.Append(text[++i]);
                }
                else
                {
                    current.Append(c);
                }
            }
        }

        if (quote is not null)
        {
            error = $"The command has an unclosed {quote} quote.";
            return null;
        }

        if (inArgument)
        {
            arguments.Add(current.ToString());
        }

        error = null;
        return arguments;
    }
}
