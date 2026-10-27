using System.Text.RegularExpressions;

namespace SharpBastion.Helper;

/// <summary>
/// Derives owner/repository from a git remote URL. The only trusted source of
/// PR coordinates — never model output. Accepts github.com only; any other
/// host returns null.
/// </summary>
public static class GitHubRemoteParser
{
    private static readonly Regex[] Patterns =
    {
        // https://github.com/owner/repo(.git) — optional embedded user[:pass]@
        new(@"^https://(?:[^@/]+@)?github\.com/([^/]+)/([^/]+?)(?:\.git)?/?$", RegexOptions.IgnoreCase | RegexOptions.Compiled),
        // git@github.com:owner/repo(.git)
        new(@"^(?:[^@/]+@)?github\.com:([^/]+)/([^/]+?)(?:\.git)?/?$", RegexOptions.IgnoreCase | RegexOptions.Compiled),
        // ssh://git@github.com(:port)/owner/repo(.git)
        new(@"^ssh://(?:[^@/]+@)?github\.com(?::\d+)?/([^/]+)/([^/]+?)(?:\.git)?/?$", RegexOptions.IgnoreCase | RegexOptions.Compiled)
    };

    public static (string Owner, string Repository)? TryParse(string? remoteUrl)
    {
        if (string.IsNullOrWhiteSpace(remoteUrl)) return null;

        var trimmed = remoteUrl.Trim();
        foreach (var pattern in Patterns)
        {
            var match = pattern.Match(trimmed);
            if (match.Success)
                return (match.Groups[1].Value, match.Groups[2].Value);
        }

        return null;
    }
}