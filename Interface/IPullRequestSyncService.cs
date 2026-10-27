using SharpBastion.Commands;
using SharpBastion.Views;

namespace SharpBastion.Interface;

public interface IPullRequestSyncService
{
    /// <summary>
    /// Processes the oldest unprocessed, trusted, actionable review comment on
    /// the pull request: asks the model, queues the proposed writes and a
    /// pending commit. Never writes, commits, pushes or replies by itself.
    /// </summary>
    Task<AskQuestionResultView> SyncNextCommentAsync(SyncPullRequestCommand command);
}