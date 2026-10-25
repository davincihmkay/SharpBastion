namespace SharpBastion.Options;

/// <summary>
/// Bound from the "GitHub" configuration section. Holds identifiers and trust
/// settings only — the API token is read from the GITHUB_TOKEN environment
/// variable by GitHubClient and is never part of configuration.
/// </summary>
public class GitHubOptions
{
    public const string SectionName = "GitHub";

    /// <summary>
    /// GitHub logins whose review comments may be processed. When non-empty,
    /// ONLY these logins are trusted. When empty, trust falls back to the
    /// comment's author_association (OWNER, MEMBER or COLLABORATOR; bots
    /// excluded). Comment text flows into model context, so this is the
    /// prompt-injection boundary.
    /// </summary>
    public List<string> AllowedCommenters { get; set; } = new();

    /// <summary>Git remote that points at the pull request's repository.</summary>
    public string RemoteName { get; set; } = "origin";

    /// <summary>
    /// Largest commented file, in bytes, that 'pr sync' will put into a prompt.
    /// The model returns the whole file, so this bounds both the prompt and the
    /// answer: set it to what your model can reliably reproduce. Larger files
    /// are skipped with a notice. Must be positive. Defaults to 25000.
    /// </summary>
    public int MaxFileBytes { get; set; } = 25_000;
}