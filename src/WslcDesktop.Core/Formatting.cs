using System.Globalization;

namespace WslcDesktop.Core;

public static class Formatting
{
    private static readonly string[] s_sizeUnits = ["B", "kB", "MB", "GB", "TB"];

    /// <summary>Decimal units, matching what the engine prints (74.8 MB).</summary>
    public static string Bytes(long bytes)
    {
        double value = bytes;
        var unit = 0;
        while (value >= 1000 && unit < s_sizeUnits.Length - 1)
        {
            value /= 1000;
            unit++;
        }

        return unit == 0
            ? string.Create(CultureInfo.InvariantCulture, $"{bytes} B")
            : string.Create(CultureInfo.InvariantCulture, $"{value:0.#} {s_sizeUnits[unit]}");
    }

    /// <summary>"just now", "5 minutes ago", "3 days ago", ...</summary>
    public static string Ago(DateTimeOffset? time, DateTimeOffset now)
    {
        if (time is not { } t)
        {
            return string.Empty;
        }

        var elapsed = now - t;
        return elapsed.TotalSeconds switch
        {
            < 60 => "just now",
            < 3600 => Plural((int)elapsed.TotalMinutes, "minute"),
            < 86400 => Plural((int)elapsed.TotalHours, "hour"),
            < 86400 * 30 => Plural((int)elapsed.TotalDays, "day"),
            < 86400 * 365 => Plural((int)(elapsed.TotalDays / 30), "month"),
            _ => Plural((int)(elapsed.TotalDays / 365), "year"),
        };

        static string Plural(int n, string unit) =>
            string.Create(CultureInfo.InvariantCulture, $"{n} {unit}{(n == 1 ? string.Empty : "s")} ago");
    }

    /// <summary>The 12-character short form of an ID, with any "sha256:" prefix removed.</summary>
    public static string ShortId(string id)
    {
        var bare = id.StartsWith("sha256:", StringComparison.Ordinal) ? id["sha256:".Length..] : id;
        return bare.Length > 12 ? bare[..12] : bare;
    }
}
