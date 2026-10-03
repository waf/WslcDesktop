using System.Text;

namespace WslcGui.Engine.Cli;

/// <summary>Formats an argument list as a command line for display (and copy/paste into a terminal).</summary>
internal static class CliCommandLine
{
    public static string Format(IEnumerable<string> arguments) => string.Join(' ', arguments.Select(Quote));

    /// <summary>Quotes an argument the way Windows' command-line parser reads it back.</summary>
    public static string Quote(string argument)
    {
        if (argument.Length > 0 && !argument.Any(c => char.IsWhiteSpace(c) || c is '"' or '\'' or '&' or '|' or '<' or '>' or '^' or ';' or '$' or '`'))
        {
            return argument;
        }

        var builder = new StringBuilder("\"");
        var backslashes = 0;
        foreach (var c in argument)
        {
            if (c == '\\')
            {
                backslashes++;
                continue;
            }

            // Backslashes are literal unless they precede a quote; a quote itself is escaped with one more.
            builder.Append('\\', c == '"' ? (backslashes * 2) + 1 : backslashes);
            backslashes = 0;
            builder.Append(c);
        }

        builder.Append('\\', backslashes * 2).Append('"');
        return builder.ToString();
    }
}
