namespace WslcGui.Core;

public enum JsonTokenKind
{
    PropertyName,
    StringValue,
    Number,
    Keyword,
    Punctuation,
}

public readonly record struct JsonToken(int Start, int Length, JsonTokenKind Kind);

/// <summary>
/// Tokenizes one line of (pretty-printed) JSON for syntax coloring. It works line by line, which is enough for
/// indented JSON (strings can't contain raw newlines), and is forgiving: unknown text is simply not colored.
/// </summary>
public static class JsonTokenizer
{
    public static List<JsonToken> TokenizeLine(ReadOnlySpan<char> line)
    {
        var tokens = new List<JsonToken>();
        var i = 0;
        while (i < line.Length)
        {
            var c = line[i];
            if (c == '"')
            {
                var start = i++;
                while (i < line.Length && line[i] != '"')
                {
                    i += line[i] == '\\' ? 2 : 1;
                }

                i = Math.Min(i + 1, line.Length);
                var next = i;
                while (next < line.Length && char.IsWhiteSpace(line[next]))
                {
                    next++;
                }

                var kind = next < line.Length && line[next] == ':' ? JsonTokenKind.PropertyName : JsonTokenKind.StringValue;
                tokens.Add(new JsonToken(start, i - start, kind));
            }
            else if (c == '-' || char.IsAsciiDigit(c))
            {
                var start = i++;
                while (i < line.Length && (char.IsAsciiDigit(line[i]) || line[i] is '.' or 'e' or 'E' or '+' or '-'))
                {
                    i++;
                }

                tokens.Add(new JsonToken(start, i - start, JsonTokenKind.Number));
            }
            else if (char.IsAsciiLetter(c))
            {
                var start = i;
                while (i < line.Length && char.IsAsciiLetter(line[i]))
                {
                    i++;
                }

                if (line[start..i] is "true" or "false" or "null")
                {
                    tokens.Add(new JsonToken(start, i - start, JsonTokenKind.Keyword));
                }
            }
            else if (c is '{' or '}' or '[' or ']' or ':' or ',')
            {
                tokens.Add(new JsonToken(i++, 1, JsonTokenKind.Punctuation));
            }
            else
            {
                i++;
            }
        }

        return tokens;
    }
}
