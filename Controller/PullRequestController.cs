using SharpBastion.Commands;
using SharpBastion.Domain;
using SharpBastion.Interface;
using SharpBastion.RequestObject;
using SharpBastion.Views;

namespace SharpBastion.Controller;

public class PullRequestController
{
    private readonly IPullRequestSyncService _pullRequestSyncService;
    private readonly IPendingCommitExecutionService _pendingCommitExecutionService;
    private readonly PendingCommitQueue _pendingCommitQueue;

    public PullRequestController(
        IPullRequestSyncService pullRequestSyncService,
        IPendingCommitExecutionService pendingCommitExecutionService,
        PendingCommitQueue pendingCommitQueue)
    {
        _pullRequestSyncService = pullRequestSyncService;
        _pendingCommitExecutionService = pendingCommitExecutionService;
        _pendingCommitQueue = pendingCommitQueue;
    }

    public AskQuestionResultView SyncNextComment(SyncPullRequestRequestObject requestObject)
    {
        var command = SyncPullRequestCommand.PopulateFromRequestObject(requestObject);
        return _pullRequestSyncService.SyncNextCommentAsync(command).GetAwaiter().GetResult();
    }

    public IReadOnlyList<PendingCommitView> ListPendingCommits() =>
        _pendingCommitQueue.ListPending()
            .Select(p => new PendingCommitView(
                p.Id,
                p.PullRequest.ToString(),
                p.ReviewCommentId,
                p.BranchName,
                p.Files
                    .Select(f => Path.GetRelativePath(p.RepositoryDirectory.Value, f.Value)
                        .Replace(Path.DirectorySeparatorChar, '/'))
                    .ToList(),
                p.CommitMessage.Value,
                p.ReplyBody.Value))
            .ToList();

    public OperationResultView ResolveCommit(ResolveCommitRequestObject requestObject)
    {
        var command = ResolvePendingCommitCommand.PopulateFromRequestObject(requestObject);

        return requestObject.Approve
            ? _pendingCommitExecutionService.ApproveAsync(command).GetAwaiter().GetResult()
            : _pendingCommitExecutionService.Reject(command);
    }
}