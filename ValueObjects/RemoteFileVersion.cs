namespace SharpBastion.ValueObjects;

/// <summary>
/// Immutable snapshot of a file's content as it exists at the head of a pull
/// request. 'pr sync' reads it from the working tree, and only after proving
/// that the tree is clean and local HEAD equals the PR head, so the content is
/// the PR-head version. Valid only for the HeadCommitSha it was taken at —
/// callers must compare that SHA against the PR's current head when reusing it.
/// </summary>
public class RemoteFileVersion
{
    public readonly PullRequestReference PullRequest;
    public readonly string HeadCommitSha;
    public readonly FileContent Content;
    public readonly DateTimeOffset FetchedAt;

    public RemoteFileVersion(
        PullRequestReference pullRequest,
        string headCommitSha,
        FileContent content,
        DateTimeOffset fetchedAt)
    {
        if (pullRequest is null)
            throw new ArgumentNullException(nameof(pullRequest));

        if (string.IsNullOrWhiteSpace(headCommitSha))
            throw new ArgumentException("Head commit SHA cannot be empty.", nameof(headCommitSha));

        if (content is null)
            throw new ArgumentNullException(nameof(content));

        PullRequest = pullRequest;
        HeadCommitSha = headCommitSha;
        Content = content;
        FetchedAt = fetchedAt;
    }

    /// <summary>True when this snapshot was taken at the given PR head commit.</summary>
    public bool IsCurrentFor(string currentHeadCommitSha) =>
        string.Equals(HeadCommitSha, currentHeadCommitSha, StringComparison.OrdinalIgnoreCase);
}