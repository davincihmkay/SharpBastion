using System.Text.RegularExpressions;

namespace SharpBastion.Helper;

/// <summary>
/// Parses simple duration strings ("30m", "24h", "2d") into a TimeSpan.
/// Intentionally minimal — no cron expressions, no compound durations.
/// </summary>
public static class IntervalParser
{
    private static readonly Regex Pattern = new(@"^(?<value>\d+)(?<unit>[smhd])$", RegexOptions.Compiled);

    public static bool TryParse(string input, out TimeSpan interval)
    {
        interval = default;
        var match = Pattern.Match(input.Trim());
        if (!match.Success) return false;

        var value = int.Parse(match.Groups["value"].Value);
        interval = match.Groups["unit"].Value switch
        {
            "s" => TimeSpan.FromSeconds(value),
            "m" => TimeSpan.FromMinutes(value),
            "h" => TimeSpan.FromHours(value),
            "d" => TimeSpan.FromDays(value),
            _ => TimeSpan.Zero
        };
        return interval > TimeSpan.Zero;
    }
}