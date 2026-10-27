using SharpBastion.ValueObjects;

namespace SharpBastion.Domain;

/// <summary>
/// In-memory, append-only record of every review-comment pass in this process.
/// The queues and the ledger forget a pass once it is resolved; this does not,
/// so what changed across several passes can be reconstructed afterwards.
/// Like every other queue here it does not survive a restart. It holds source
/// code and comment text: treat it as sensitive.
///
/// Mutable state is private. Callers refer to a pass by its sequence number and
/// read it back as immutable snapshots. Recording never throws for an unknown or
/// evicted pass: journaling must not be able to break a sync or a review.
/// </summary>
public class PassJournal
{
    public const int MaxPasses = 200;

    private const int MaxBodyLength = 4000;
    private const int MaxDetailLength = 2000;

    private static readonly StringComparer PathComparer =
        OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;

    private readonly object _gate = new();
    private readonly List<Entry> _entries = new();
    private int _nextSequence = 1;

    public int Count
    {
        get
        {
            lock (_gate) return _entries.Count;
        }
    }

    /// <summary>Starts a pass in state Started and returns its sequence number.</summary>
    public int Begin(PullRequestReference pullRequest, string branch, PassComment comment)
    {
        lock (_gate)
        {
            var now = DateTimeOffset.UtcNow;
            var entry = new Entry(
                _nextSequence++,
                pullRequest,
                branch,
                comment with { Body = Truncate(comment.Body, MaxBodyLength) ?? string.Empty },
                now);

            _entries.Add(entry);

            if (_entries.Count > MaxPasses)
                _entries.RemoveAt(0);

            return entry.Sequence;
        }
    }

    /// <summary>Stores what the model proposed (possibly nothing) and its summary line.</summary>
    public void RecordProposal(int pass, string? modelSummary, IEnumerable<PassFileChange> files)
    {
        var changes = files.ToList();

        Update(pass, entry =>
        {
            entry.ModelSummary = Truncate(modelSummary, MaxDetailLength);
            entry.Files.Clear();
            entry.Files.AddRange(changes);
        });
    }

    /// <summary>Ends a pass that never reached the commit queue.</summary>
    public void Conclude(int pass, PassState state, string? detail = null) =>
        Update(pass, entry =>
        {
            entry.State = state;
            entry.Detail = Truncate(detail, MaxDetailLength);
        });

    /// <summary>Links the pass to its queued commit and marks it Proposed.</summary>
    public void AttachCommit(int pass, string commitId) =>
        Update(pass, entry =>
        {
            entry.CommitId = commitId;
            entry.State = PassState.Proposed;
        });

    /// <summary>
    /// Records the user's decision on one proposed file. Matches the most recent
    /// pass still awaiting review that proposed this path. Returns false when no
    /// pass did (for example a write proposed by 'ask').
    /// </summary>
    public bool RecordFileDecision(string absolutePath, bool approved)
    {
        var normalized = Path.GetFullPath(absolutePath);

        lock (_gate)
        {
            for (var i = _entries.Count - 1; i >= 0; i--)
            {
                var entry = _entries[i];
                if (entry.State != PassState.Proposed) continue;

                var index = entry.Files.FindIndex(f =>
                    f.Status == PassFileStatus.Proposed && PathComparer.Equals(f.AbsolutePath, normalized));
                if (index < 0) continue;

                entry.Files[index] = entry.Files[index] with
                {
                    Status = approved ? PassFileStatus.Approved : PassFileStatus.Rejected
                };
                entry.UpdatedAt = DateTimeOffset.UtcNow;
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Records what happened to the commit. The SHA and posted reply are kept
    /// once set; the detail always reflects the latest call.
    /// </summary>
    public bool RecordCommitOutcome(
        string commitId, PassState state, string? sha = null, string? detail = null, string? postedReply = null)
    {
        lock (_gate)
        {
            var entry = _entries.FirstOrDefault(e => e.CommitId == commitId);
            if (entry is null) return false;

            entry.State = state;
            entry.Detail = Truncate(detail, MaxDetailLength);
            if (sha is not null) entry.CommitSha = sha;
            if (postedReply is not null) entry.PostedReply = postedReply;
            entry.UpdatedAt = DateTimeOffset.UtcNow;
            return true;
        }
    }

    /// <summary>Every recorded pass, oldest first, as immutable snapshots.</summary>
    public IReadOnlyList<PassSnapshot> List()
    {
        lock (_gate)
            return _entries.Select(ToSnapshot).ToList();
    }

    public PassSnapshot? Find(int sequence)
    {
        lock (_gate)
        {
            var entry = _entries.FirstOrDefault(e => e.Sequence == sequence);
            return entry is null ? null : ToSnapshot(entry);
        }
    }

    // ── Internals ────────────────────────────────────────────────────────────

    private void Update(int pass, Action<Entry> change)
    {
        lock (_gate)
        {
            var entry = _entries.FirstOrDefault(e => e.Sequence == pass);
            if (entry is null) return;

            change(entry);
            entry.UpdatedAt = DateTimeOffset.UtcNow;
        }
    }

    private static PassSnapshot ToSnapshot(Entry e) =>
        new(e.Sequence, e.PullRequest, e.Branch, e.Comment, e.StartedAt, e.UpdatedAt, e.State,
            e.Detail, e.ModelSummary, e.CommitId, e.CommitSha, e.PostedReply, e.Files.ToList());

    private static string? Truncate(string? text, int max) =>
        text is not null && text.Length > max ? text[..max] + " [TRUNCATED]" : text;

    private sealed class Entry
    {
        public Entry(int sequence, PullRequestReference pullRequest, string branch, PassComment comment, DateTimeOffset now)
        {
            Sequence = sequence;
            PullRequest = pullRequest;
            Branch = branch;
            Comment = comment;
            StartedAt = now;
            UpdatedAt = now;
        }

        public int Sequence { get; }
        public PullRequestReference PullRequest { get; }
        public string Branch { get; }
        public PassComment Comment { get; }
        public DateTimeOffset StartedAt { get; }
        public DateTimeOffset UpdatedAt { get; set; }
        public PassState State { get; set; } = PassState.Started;
        public string? Detail { get; set; }
        public string? ModelSummary { get; set; }
        public string? CommitId { get; set; }
        public string? CommitSha { get; set; }
        public string? PostedReply { get; set; }
        public List<PassFileChange> Files { get; } = new();
    }
}