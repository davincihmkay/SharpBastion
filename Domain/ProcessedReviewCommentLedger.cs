using System.Collections.Concurrent;
using SharpBastion.ValueObjects;

namespace SharpBastion.Domain;

/// <summary>
/// In-memory record of review comments already handled (committed, declined,
/// or skipped) in this process. Like every other queue here it does not
/// survive a restart — a restart falls back to the "token user already
/// replied" check in PullRequestSyncService.
/// </summary>
public class ProcessedReviewCommentLedger
{
    private readonly ConcurrentDictionary<string, byte> _processed =
        new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Number of comments handled so far in this process. Lets a caller detect
    /// whether a sync made progress without inspecting message text.
    /// </summary>
    public int Count => _processed.Count;

    public void MarkProcessed(PullRequestReference pullRequest, long reviewCommentId) =>
        _processed.TryAdd(Key(pullRequest, reviewCommentId), 0);

    public bool IsProcessed(PullRequestReference pullRequest, long reviewCommentId) =>
        _processed.ContainsKey(Key(pullRequest, reviewCommentId));

    private static string Key(PullRequestReference pullRequest, long reviewCommentId) =>
        $"{pullRequest}:{reviewCommentId}";
}