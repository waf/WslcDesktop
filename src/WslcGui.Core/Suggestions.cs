namespace WslcGui.Core;

/// <summary>Ranks autocomplete candidates for typed text. The user can always type something not in the list.</summary>
public static class Suggestions
{
    public const int DefaultMax = 8;

    /// <summary>
    /// Candidates matching <paramref name="text"/>: those starting with it first, then those containing it, each
    /// group in the original order. An exact match is left out (nothing to complete). Empty text matches everything.
    /// </summary>
    public static IReadOnlyList<string> Filter(IEnumerable<string> candidates, string text, int max = DefaultMax)
    {
        var query = text.Trim();
        var prefix = new List<string>();
        var contains = new List<string>();
        foreach (var candidate in candidates.Distinct(StringComparer.Ordinal))
        {
            if (candidate.Equals(query, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (candidate.StartsWith(query, StringComparison.OrdinalIgnoreCase))
            {
                prefix.Add(candidate);
            }
            else if (candidate.Contains(query, StringComparison.OrdinalIgnoreCase))
            {
                contains.Add(candidate);
            }
        }

        return prefix.Concat(contains).Take(max).ToList();
    }

    /// <summary>
    /// Whether a volume source looks like a host path (<c>C:\data</c>, <c>\\server\share</c>, <c>./data</c>, <c>/mnt/x</c>)
    /// rather than a volume name, so volume-name suggestions shouldn't be offered.
    /// </summary>
    public static bool LooksLikePath(string text)
    {
        var value = text.Trim();
        return value.Length > 0
            && (value.Contains('\\', StringComparison.Ordinal)
                || value.Contains('/', StringComparison.Ordinal)
                || value.StartsWith('.')
                || (value.Length >= 2 && char.IsAsciiLetter(value[0]) && value[1] == ':'));
    }
}
