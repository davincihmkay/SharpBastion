namespace SharpBastion.Views;

public class PendingCommitView
{
    public readonly string Id;
    public readonly string PullRequest;
    public readonly long ReviewCommentId;
    public readonly string Branch;
    public readonly IReadOnlyList<string> Files;
    public readonly string CommitMessage;
    public readonly string ReplyBody;

    public PendingCommitView(
        string id,
        string pullRequest,
        long reviewCommentId,
        string branch,
        IReadOnlyList<string> files,
        string commitMessage,
        string replyBody)
    {
        Id = id;
        PullRequest = pullRequest;
        ReviewCommentId = reviewCommentId;
        Branch = branch;
        Files = files;
        CommitMessage = commitMessage;
        ReplyBody = replyBody;
    }
}