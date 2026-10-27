using SharpBastion.ValueObjects;

namespace SharpBastion.Domain;

/// <summary>
/// Where a review-comment pass stands. A pass is one attempt to address one
/// review comment, from the moment it is picked up to its final outcome.
/// </summary>
public enum PassState
{
    /// <summary>Picked up; the model call is in flight (or was interrupted).</summary>
    Started,

    /// <summary>Comment failed validation before the model was asked.</summary>
    Skipped,

    /// <summary>The model call failed. The comment stays open.</summary>
    AskFailed,

    /// <summary>The model proposed no file change.</summary>
    NoChange,

    /// <summary>The proposal failed validation and was discarded as a whole.</summary>
    Discarded,

    /// <summary>Writes and commit queued; waiting for review.</summary>
    Proposed,

    /// <summary>The user rejected the commit.</summary>
    Rejected,

    /// <summary>Commit approved, but no approved change was on disk.</summary>
    NothingToCommit,

    /// <summary>Staging or committing failed.</summary>
    CommitFailed,

    /// <summary>Committed locally; the push failed.</summary>
    PushFailed,

    /// <summary>Pushed. On its own (with a detail) this means the reply was not posted.</summary>
    Pushed,

    /// <summary>Pushed and replied.</summary>
    Replied
}

public enum PassFileStatus
{
    Proposed,
    Approved,
    Rejected,
    Discarded
}

/// <summary>The review comment that triggered a pass. The body is untrusted text.</summary>
public sealed record PassComment(long Id, string? Author, string? Path, string Body, string? Url);

/// <summary>
/// One file in a pass. Baseline is the file on disk when the proposal was made
/// (null when the file did not exist, or was too large or unreadable to record;
/// ExistedBefore tells them apart). Proposed is the complete proposed content.
/// </summary>
public sealed record PassFileChange(
    string AbsolutePath,
    string RelativePath,
    bool ExistedBefore,
    string? Baseline,
    string Proposed,
    PassFileStatus Status);

/// <summary>Immutable view of one pass at the time it was read.</summary>
public sealed record PassSnapshot(
    int Sequence,
    PullRequestReference PullRequest,
    string Branch,
    PassComment Comment,
    DateTimeOffset StartedAt,
    DateTimeOffset UpdatedAt,
    PassState State,
    string? Detail,
    string? ModelSummary,
    string? CommitId,
    string? CommitSha,
    string? PostedReply,
    IReadOnlyList<PassFileChange> Files);