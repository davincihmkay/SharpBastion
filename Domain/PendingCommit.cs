using SharpBastion.ValueObjects;

namespace SharpBastion.Domain;

/// <summary>
/// A proposed commit-and-push that addresses one PR review comment. Queued
/// alongside the file writes it covers; at execution time only the files
/// listed here that actually changed on disk may be staged — anything else
/// changed in the tree aborts the commit.
/// </summary>
public class PendingCommit
{
    public string Id { get; }
    public LocalPath RepositoryDirectory { get; }
    public PullRequestReference PullRequest { get; }
    public long ReviewCommentId { get; }
    public IReadOnlyList<LocalPath> Files { get; }
    public Message CommitMessage { get; }

    /// <summary>The PR head branch to push to. Never the base branch.</summary>
    public string BranchName { get; }

    /// <summary>Reply text; the executor appends the resulting commit SHA.</summary>
    public Message ReplyBody { get; }

    public PendingCommit(
        LocalPath repositoryDirectory,
        PullRequestReference pullRequest,
        long reviewCommentId,
        IReadOnlyList<LocalPath> files,
        Message commitMessage,
        string branchName,
        Message replyBody)
    {
        if (repositoryDirectory is null) throw new ArgumentNullException(nameof(repositoryDirectory));
        if (pullRequest is null) throw new ArgumentNullException(nameof(pullRequest));
        if (files is null || files.Count == 0)
            throw new ArgumentException("A pending commit must include at least one file.", nameof(files));
        if (string.IsNullOrWhiteSpace(commitMessage?.Value))
            throw new ArgumentException("Commit message cannot be empty.", nameof(commitMessage));
        if (string.IsNullOrWhiteSpace(branchName))
            throw new ArgumentException("Branch name cannot be empty.", nameof(branchName));
        if (string.IsNullOrWhiteSpace(replyBody?.Value))
            throw new ArgumentException("Reply body cannot be empty.", nameof(replyBody));

        Id = Guid.NewGuid().ToString("N");
        RepositoryDirectory = repositoryDirectory;
        PullRequest = pullRequest;
        ReviewCommentId = reviewCommentId;
        Files = files;
        CommitMessage = commitMessage;
        BranchName = branchName;
        ReplyBody = replyBody;
    }
}