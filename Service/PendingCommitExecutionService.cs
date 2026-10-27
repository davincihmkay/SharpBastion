using System.Runtime.InteropServices;
using Microsoft.Extensions.Options;
using SharpBastion.Commands;
using SharpBastion.Domain;
using SharpBastion.Interface;
using SharpBastion.Options;
using SharpBastion.Views;

namespace SharpBastion.Service;

public class PendingCommitExecutionService : IPendingCommitExecutionService
{
    private const int MaxCustomReplyLength = 4000;

    // The footer is a claim made on the user's behalf, so each one must be true
    // on every path that can produce it. A generated reply is model-drafted and
    // the change behind it was approved by the user; a custom reply is the
    // user's own words. Both were sent by SharpBastion, so both say so.
    private const string GeneratedReplyFooter = "(AI drafted, human approved · Sent using SharpBastion)";
    private const string CustomReplyFooter = "(Human written · Sent using SharpBastion)";

    private static readonly StringComparer PathComparer =
        RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
            ? StringComparer.OrdinalIgnoreCase
            : StringComparer.Ordinal;

    private readonly PendingCommitQueue _pendingCommitQueue;
    private readonly IGitCliClient _gitCliClient;
    private readonly IGitHubClient _gitHubClient;
    private readonly ProcessedReviewCommentLedger _ledger;
    private readonly GitHubOptions _options;

    public PendingCommitExecutionService(
        PendingCommitQueue pendingCommitQueue,
        IGitCliClient gitCliClient,
        IGitHubClient gitHubClient,
        ProcessedReviewCommentLedger ledger,
        IOptions<GitHubOptions> options)
    {
        _pendingCommitQueue = pendingCommitQueue;
        _gitCliClient = gitCliClient;
        _gitHubClient = gitHubClient;
        _ledger = ledger;
        _options = options.Value;
    }

    public async Task<OperationResultView> ApproveAsync(ResolvePendingCommitCommand command)
    {
        // Look up without removing: a failed precondition leaves the entry
        // queued so the user can fix the tree and retry.
        var commit = _pendingCommitQueue.ListPending().SingleOrDefault(p => p.Id == command.Id);
        if (commit is null)
            return new OperationResultView(false, $"No pending commit found for '{command.Id}'. Already resolved or unknown.");

        // Checked before anything is staged, so an over-long reply never leaves a half-done commit.
        var customReply = string.IsNullOrWhiteSpace(command.CustomReply) ? null : command.CustomReply.Trim();
        if (customReply is not null && customReply.Length > MaxCustomReplyLength)
            return new OperationResultView(false,
                $"Custom reply is {customReply.Length} characters; the limit is {MaxCustomReplyLength}. Commit left pending.");

        var directory = commit.RepositoryDirectory.Value;

        var branch = await _gitCliClient.GetCurrentBranchAsync(directory);
        if (!string.Equals(branch, commit.BranchName, StringComparison.Ordinal))
            return new OperationResultView(false,
                $"Checked-out branch '{branch}' is not '{commit.BranchName}'. Commit left pending.");

        var allowed = commit.Files.ToDictionary(
            f => ToRepositoryRelative(directory, f.Value), f => f.Value, PathComparer);

        var changed = await _gitCliClient.GetChangedPathsAsync(directory);

        var unexpected = changed.Where(c => !allowed.ContainsKey(c)).ToList();
        if (unexpected.Count > 0)
            return new OperationResultView(false,
                $"Unexpected changes outside the approved files: {string.Join(", ", unexpected)}. " +
                "Nothing was staged. Commit left pending.");

        var toStage = changed.Select(c => allowed[c]).ToList();

        if (!_pendingCommitQueue.TryResolve(commit.Id, out _))
            return new OperationResultView(false, $"Pending commit '{command.Id}' was already resolved.");

        if (toStage.Count == 0)
        {
            _ledger.MarkProcessed(commit.PullRequest, commit.ReviewCommentId);
            return new OperationResultView(false,
                $"No approved changes on disk for comment {commit.ReviewCommentId} (all writes rejected or unchanged). " +
                "Marked processed; nothing committed or posted.");
        }

        string sha;
        try
        {
            await _gitCliClient.StageAsync(directory, toStage);
            sha = await _gitCliClient.CommitAsync(directory, commit.CommitMessage.Value);
        }
        catch (Exception ex)
        {
            return new OperationResultView(false,
                $"Commit failed: {ex.Message} Staged changes may remain — inspect with 'git status'.");
        }

        try
        {
            await _gitCliClient.PushAsync(directory, _options.RemoteName, commit.BranchName);
        }
        catch (Exception ex)
        {
            return new OperationResultView(false,
                $"Committed locally as {Short(sha)} but the push failed: {ex.Message} Nothing was posted to the pull request.");
        }

        _ledger.MarkProcessed(commit.PullRequest, commit.ReviewCommentId);

        try
        {
            await _gitHubClient.ReplyToReviewCommentAsync(
                commit.PullRequest,
                commit.ReviewCommentId,
                BuildReply(commit, customReply, sha));
        }
        catch (Exception ex)
        {
            return new OperationResultView(true,
                $"Pushed {Short(sha)} to '{commit.BranchName}'. [NOTICE] Reply on comment {commit.ReviewCommentId} failed: {ex.Message}");
        }

        return new OperationResultView(true,
            $"Pushed {Short(sha)} to '{commit.BranchName}' and replied on comment {commit.ReviewCommentId}" +
            (customReply is null ? "." : " with your custom reply."));
    }

    public OperationResultView Reject(ResolvePendingCommitCommand command)
    {
        if (!_pendingCommitQueue.TryResolve(command.Id, out var commit) || commit is null)
            return new OperationResultView(false, $"No pending commit found for '{command.Id}'. Already resolved or unknown.");

        _ledger.MarkProcessed(commit.PullRequest, commit.ReviewCommentId);

        var files = string.Join(", ", commit.Files.Select(f => ToRepositoryRelative(commit.RepositoryDirectory.Value, f.Value)));
        return new OperationResultView(true,
            $"Rejected commit for comment {commit.ReviewCommentId}. Any approved file writes remain uncommitted in the working tree: {files}. " +
            "Comment marked processed for this session.");
    }

    /// <summary>
    /// Both forms end with the commit SHA, which only exists after the commit,
    /// then an attribution footer: generated replies say they were AI drafted
    /// and human approved; a reply the user typed says it is human written.
    /// Both say they were sent using SharpBastion.
    /// </summary>
    private static string BuildReply(PendingCommit commit, string? customReply, string sha)
    {
        var addressed = $"Addressed in {Short(sha)}.";

        return customReply is null
            ? $"{commit.ReplyBody.Value}\n\n{addressed}\n\n{GeneratedReplyFooter}"
            : $"{customReply}\n\n{addressed}\n\n{CustomReplyFooter}";
    }

    private static string ToRepositoryRelative(string directory, string absolutePath) =>
        Path.GetRelativePath(directory, absolutePath).Replace(Path.DirectorySeparatorChar, '/');

    private static string Short(string sha) => sha.Length > 7 ? sha[..7] : sha;
}