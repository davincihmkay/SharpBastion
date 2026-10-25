using System.Collections.Concurrent;

namespace SharpBastion.Domain;

public class PendingCommitQueue
{
    private readonly ConcurrentDictionary<string, PendingCommit> _pending =
        new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// One pending entry per (pull request, review comment). Returns false and
    /// skips — not stacks — when one is already pending.
    /// </summary>
    public bool Enqueue(PendingCommit commit)
    {
        var isDuplicate = _pending.Values.Any(p =>
            p.PullRequest == commit.PullRequest &&
            p.ReviewCommentId == commit.ReviewCommentId);

        return !isDuplicate && _pending.TryAdd(commit.Id, commit);
    }

    public IReadOnlyCollection<PendingCommit> ListPending() => _pending.Values.ToList();

    public bool TryResolve(string id, out PendingCommit? commit) => _pending.TryRemove(id, out commit);

    public int Count => _pending.Count;
}