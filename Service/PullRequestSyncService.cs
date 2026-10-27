using System.Text;
using Microsoft.Extensions.Options;
using SharpBastion.ClientResponseObject;
using SharpBastion.Commands;
using SharpBastion.Domain;
using SharpBastion.Helper;
using SharpBastion.Interface;
using SharpBastion.Options;
using SharpBastion.PromptBuilder;
using SharpBastion.ValueObjects;
using SharpBastion.Views;
using File = SharpBastion.Domain.File;

namespace SharpBastion.Service;

public class PullRequestSyncService : IPullRequestSyncService
{
    private const string ReingestNotice =
        "Repository context was ingested earlier — re-run 'ingest' if the checkout changed since.";

    private static readonly HashSet<string> TrustedAssociations =
        new(StringComparer.OrdinalIgnoreCase) { "OWNER", "MEMBER", "COLLABORATOR" };

    private readonly IGitHubClient _gitHubClient;
    private readonly IGitCliClient _gitCliClient;
    private readonly IRepositoryIngestorService _repositoryIngestorService;
    private readonly PendingWriteQueue _pendingWriteQueue;
    private readonly PendingCommitQueue _pendingCommitQueue;
    private readonly ProcessedReviewCommentLedger _ledger;
    private readonly PassJournal _journal;
    private readonly GitHubOptions _options;

    public PullRequestSyncService(
        IGitHubClient gitHubClient,
        IGitCliClient gitCliClient,
        IRepositoryIngestorService repositoryIngestorService,
        PendingWriteQueue pendingWriteQueue,
        PendingCommitQueue pendingCommitQueue,
        ProcessedReviewCommentLedger ledger,
        PassJournal journal,
        IOptions<GitHubOptions> options)
    {
        _gitHubClient = gitHubClient;
        _gitCliClient = gitCliClient;
        _repositoryIngestorService = repositoryIngestorService;
        _pendingWriteQueue = pendingWriteQueue;
        _pendingCommitQueue = pendingCommitQueue;
        _ledger = ledger;
        _journal = journal;
        _options = options.Value;
    }

    // ── Entry point ──────────────────────────────────────────────────────────

    public async Task<AskQuestionResultView> SyncNextCommentAsync(SyncPullRequestCommand command)
    {
        try
        {
            return await RunSyncAsync(command);
        }
        catch (SyncPreconditionException ex)
        {
            // Only our own precondition failures are turned into a result.
            // Anything else (HTTP, git, bugs) propagates to the caller.
            return new AskQuestionResultView(false, ex.Message);
        }
    }

    // ── Pipeline ─────────────────────────────────────────────────────────────

    private async Task<AskQuestionResultView> RunSyncAsync(SyncPullRequestCommand command)
    {
        var notices = new List<string>();

        // 0. Fail fast on configuration nobody explicitly meant.
        EnsureConfigurationValid();

        // 1. Nothing unreviewed may be pending.
        EnsureNoPendingItems();

        // 2-3. Identify the pull request from git, then check it is safe to work on.
        var target = await ResolveTargetAsync(command);

        // 4. Working tree, local HEAD, remote branch and PR head must all agree.
        await EnsureCheckoutAlignedAsync(target);

        notices.Add(ReingestNotice);

        // 5. Trusted, unhandled, top-level review comments, oldest first.
        var selection = await SelectCommentsAsync(target, notices);

        // 6. The first comment that can actually be worked on wins.
        for (var index = 0; index < selection.Candidates.Count; index++)
        {
            var comment = selection.Candidates[index];

            var skipReason = ValidateCommentTarget(target, comment);
            RemoteFileVersion? currentFile = null;
            if (skipReason is null)
                (currentFile, skipReason) = await ReadWorkingTreeFileAsync(target, comment);

            if (skipReason is not null)
            {
                SkipComment(target, comment, skipReason, notices);
                continue;
            }

            var remaining = selection.Candidates.Count - index - 1;
            return ProposeChange(target, comment, selection.AllComments, currentFile!, remaining, notices);
        }

        return new AskQuestionResultView(true, "No unprocessed actionable review comments.", notices);
    }

    // ── Steps 0-1: configuration and pending items ───────────────────────────

    private void EnsureConfigurationValid()
    {
        if (_options.MaxFileBytes <= 0)
            throw new SyncPreconditionException(
                $"'GitHub:MaxFileBytes' is {_options.MaxFileBytes}, which is not positive. Fix it in appsettings.json.");

        if (_options.MaxFilesPerComment <= 0)
            throw new SyncPreconditionException(
                $"'GitHub:MaxFilesPerComment' is {_options.MaxFilesPerComment}, which is not positive. Fix it in appsettings.json.");
    }

    /// <summary>
    /// Serial processing: never stack a new proposal on unreviewed ones. This
    /// also guarantees that every pending write after the model call came from it.
    /// </summary>
    private void EnsureNoPendingItems()
    {
        if (_pendingWriteQueue.Count == 0 && _pendingCommitQueue.Count == 0)
            return;

        throw new SyncPreconditionException(
            $"Resolve pending items first: {_pendingWriteQueue.Count} write(s), " +
            $"{_pendingCommitQueue.Count} commit(s). Run 'review'.");
    }

    // ── Steps 2-3: target and pull request guards ────────────────────────────

    private async Task<SyncTarget> ResolveTargetAsync(SyncPullRequestCommand command)
    {
        var repositoryDirectory = command.RepositoryPath.Value;
        var remote = _options.RemoteName;

        var branch = await _gitCliClient.GetCurrentBranchAsync(repositoryDirectory);
        if (branch == "HEAD")
            throw new SyncPreconditionException("Detached HEAD — check out the pull request's head branch first.");

        // Owner and repository come from the git remote, never from model output.
        var parsedRemote = GitHubRemoteParser.TryParse(
            await _gitCliClient.GetRemoteUrlAsync(repositoryDirectory, remote));
        if (parsedRemote is null)
            throw new SyncPreconditionException($"Remote '{remote}' is not a github.com repository.");

        var pullRequest = new PullRequestReference(
            parsedRemote.Value.Owner, parsedRemote.Value.Repository, command.PullRequestNumber);

        var pull = await _gitHubClient.GetPullRequestAsync(pullRequest);

        EnsurePullRequestEligible(pullRequest, pull, branch);

        return new SyncTarget(repositoryDirectory, remote, pullRequest, pull);
    }

    private static void EnsurePullRequestEligible(
        PullRequestReference pullRequest, GitHubPullRequestResponseObject pull, string checkedOutBranch)
    {
        if (!string.Equals(pull.State, "open", StringComparison.OrdinalIgnoreCase))
            throw new SyncPreconditionException($"{pullRequest} is {pull.State}, not open.");

        if (pull.IsFromFork)
            throw new SyncPreconditionException(
                $"{pullRequest} comes from a fork (or a deleted fork) — pushing to it is not supported.");

        if (!string.Equals(pull.Head.Ref, checkedOutBranch, StringComparison.Ordinal))
            throw new SyncPreconditionException(
                $"Checked-out branch '{checkedOutBranch}' is not the pull request's head branch '{pull.Head.Ref}'.");

        var defaultBranch = pull.Base.Repository?.DefaultBranch;
        if (string.Equals(pull.Head.Ref, defaultBranch, StringComparison.Ordinal) ||
            string.Equals(pull.Head.Ref, pull.Base.Ref, StringComparison.Ordinal))
            throw new SyncPreconditionException(
                $"Refusing to operate on '{pull.Head.Ref}': it is the base/default branch.");
    }

    // ── Step 4: checkout alignment ───────────────────────────────────────────

    /// <summary>
    /// Requires: clean tree, and local HEAD == remote branch == PR head SHA.
    /// When this passes, every tracked file on disk IS its PR-head version,
    /// which is what lets the prompt use working-tree content.
    /// </summary>
    private async Task EnsureCheckoutAlignedAsync(SyncTarget target)
    {
        var changed = await _gitCliClient.GetChangedPathsAsync(target.RepositoryDirectory);
        if (changed.Count > 0)
            throw new SyncPreconditionException(
                $"Working tree is not clean ({changed.Count} change(s), e.g. '{changed[0]}'). Commit or stash first.");

        var headRef = target.Pull.Head.Ref;
        await _gitCliClient.FetchBranchAsync(target.RepositoryDirectory, target.Remote, headRef);

        var remoteSha = await _gitCliClient.ResolveCommitAsync(
            target.RepositoryDirectory, $"{target.Remote}/{headRef}");
        var localSha = await _gitCliClient.ResolveCommitAsync(target.RepositoryDirectory, "HEAD");

        if (!SameSha(remoteSha, target.Pull.Head.Sha))
            throw new SyncPreconditionException("The pull request head moved while syncing. Re-run the command.");

        if (SameSha(localSha, remoteSha))
            return;

        throw new SyncPreconditionException(await DescribeMisalignmentAsync(target, localSha, remoteSha));
    }

    private async Task<string> DescribeMisalignmentAsync(SyncTarget target, string localSha, string remoteSha)
    {
        var headRef = target.Pull.Head.Ref;

        if (await _gitCliClient.IsAncestorAsync(target.RepositoryDirectory, localSha, remoteSha))
            return $"Local branch is behind {target.Remote}/{headRef}. Run 'git pull --ff-only', then re-ingest.";

        if (await _gitCliClient.IsAncestorAsync(target.RepositoryDirectory, remoteSha, localSha))
            return "Local branch has unpushed commits. Push or reset them first.";

        return $"Local branch has diverged from {target.Remote}/{headRef}. Resolve manually.";
    }

    // ── Step 5: comment selection ────────────────────────────────────────────

    private async Task<CommentSelection> SelectCommentsAsync(SyncTarget target, List<string> notices)
    {
        var actor = await _gitHubClient.GetAuthenticatedUserAsync();
        var allComments = await _gitHubClient.ListReviewCommentsAsync(target.PullRequest);

        var open = FindOpenComments(target.PullRequest, allComments, actor.Login);

        var candidates = open.Where(IsTrusted).ToList();
        if (open.Count > candidates.Count)
            notices.Add($"{open.Count - candidates.Count} comment(s) skipped: author not trusted (see GitHub:AllowedCommenters).");

        return new CommentSelection(allComments, candidates);
    }

    /// <summary>
    /// Top-level comments that are not ours, not already answered by us (which
    /// also survives a restart), and not already handled in this process.
    /// </summary>
    private List<GitHubReviewCommentResponseObject> FindOpenComments(
        PullRequestReference pullRequest,
        IReadOnlyList<GitHubReviewCommentResponseObject> allComments,
        string actorLogin)
    {
        var alreadyRepliedRoots = allComments
            .Where(c => c.InReplyToId.HasValue && SameLogin(c.User?.Login, actorLogin))
            .Select(c => c.InReplyToId!.Value)
            .ToHashSet();

        return allComments
            .Where(c => c.InReplyToId is null &&
                        !alreadyRepliedRoots.Contains(c.Id) &&
                        !_ledger.IsProcessed(pullRequest, c.Id) &&
                        !SameLogin(c.User?.Login, actorLogin))
            .OrderBy(c => c.CreatedAt)
            .ToList();
    }

    /// <summary>
    /// The prompt-injection boundary. A non-empty AllowedCommenters list is
    /// exclusive; otherwise trust falls back to author association, bots excluded.
    /// </summary>
    private bool IsTrusted(GitHubReviewCommentResponseObject comment)
    {
        var login = comment.User?.Login;
        if (string.IsNullOrWhiteSpace(login)) return false;

        if (_options.AllowedCommenters.Count > 0)
            return _options.AllowedCommenters.Contains(login, StringComparer.OrdinalIgnoreCase);

        return !string.Equals(comment.User?.Type, "Bot", StringComparison.OrdinalIgnoreCase) &&
               TrustedAssociations.Contains(comment.AuthorAssociation);
    }

    // ── Step 6: per-comment checks (return values: a skip continues the loop) ─

    /// <summary>Returns why the comment cannot be worked on, or null if it can.</summary>
    private static string? ValidateCommentTarget(SyncTarget target, GitHubReviewCommentResponseObject comment)
    {
        if (string.IsNullOrWhiteSpace(comment.Path) || string.IsNullOrWhiteSpace(comment.Body))
            return "no file path or empty body";

        var absolutePath = ToAbsolutePath(target, comment.Path);
        if (!PathGuard.IsWithinRoot(target.RepositoryDirectory, absolutePath))
            return $"'{comment.Path}' resolves outside the repository";

        var extension = Path.GetExtension(comment.Path);
        var fileName = Path.GetFileName(comment.Path);
        if (!File.AllowedExtensions.Contains(extension) && !File.AllowedFileNames.Contains(fileName))
            return $"file type '{extension}' is not in the write allowlist";

        return null;
    }

    /// <summary>
    /// The commented file as it is on disk right now, or a reason it can't be used.
    /// EnsureCheckoutAlignedAsync has already proved the tree is clean and equals the
    /// PR head, so this IS the PR-head version — no API call and no staleness from
    /// the ingested snapshot.
    /// </summary>
    private async Task<(RemoteFileVersion? File, string? SkipReason)> ReadWorkingTreeFileAsync(
        SyncTarget target, GitHubReviewCommentResponseObject comment)
    {
        var absolutePath = ToAbsolutePath(target, comment.Path);

        if (!System.IO.File.Exists(absolutePath))
            return (null, $"'{comment.Path}' does not exist in the working tree");

        // A link could point outside the repository; never read or later overwrite through one.
        if (ContainsSymbolicLink(target.RepositoryDirectory, absolutePath))
            return (null, $"'{comment.Path}' is, or sits under, a symbolic link");

        var length = new FileInfo(absolutePath).Length;
        if (length > _options.MaxFileBytes)
            return (null, $"'{comment.Path}' exceeds {_options.MaxFileBytes} bytes");

        try
        {
            var text = await System.IO.File.ReadAllTextAsync(absolutePath);

            var snapshot = new RemoteFileVersion(
                target.PullRequest, target.Pull.Head.Sha, new FileContent(text), DateTimeOffset.UtcNow);

            return (snapshot, null);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return (null, $"'{comment.Path}' could not be read: {ex.Message}");
        }
    }

    private static string ToAbsolutePath(SyncTarget target, string repositoryRelativePath) =>
        Path.GetFullPath(Path.Combine(target.RepositoryDirectory, repositoryRelativePath));

    private static string ToRepositoryRelative(string repositoryDirectory, string absolutePath) =>
        Path.GetRelativePath(repositoryDirectory, absolutePath)
            .Replace(Path.DirectorySeparatorChar, '/');

    /// <summary>True when the file, or any directory between the repository root and it, is a link.</summary>
    private static bool ContainsSymbolicLink(string repositoryDirectory, string absolutePath)
    {
        var root = Path.GetFullPath(repositoryDirectory)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

        var relative = Path.GetRelativePath(root, absolutePath);
        var current = root;

        foreach (var segment in relative.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar))
        {
            current = Path.Combine(current, segment);

            FileSystemInfo info = Directory.Exists(current)
                ? new DirectoryInfo(current)
                : new FileInfo(current);

            if (info.LinkTarget is not null)
                return true;
        }

        return false;
    }

    /// <summary>Marks the comment processed so it is not picked again, and tells the user why.</summary>
    private void SkipComment(
        SyncTarget target,
        GitHubReviewCommentResponseObject comment,
        string reason,
        List<string> notices)
    {
        _ledger.MarkProcessed(target.PullRequest, comment.Id);

        var pass = BeginPass(target, comment);
        _journal.Conclude(pass, PassState.Skipped, reason);

        notices.Add($"Skipped comment {comment.Id} ({reason}).");
    }

    // ── Steps 7-8: ask the model, validate the proposal, queue the commit ────

    private AskQuestionResultView ProposeChange(
        SyncTarget target,
        GitHubReviewCommentResponseObject comment,
        IReadOnlyList<GitHubReviewCommentResponseObject> allComments,
        RemoteFileVersion currentFile,
        int remainingCandidates,
        List<string> notices)
    {
        var pass = BeginPass(target, comment);

        var result = AskModel(target, comment, allComments, currentFile);
        notices.AddRange(result.Notices);

        if (!result.Success)
        {
            _journal.Conclude(pass, PassState.AskFailed, result.DisplayMessage);
            return new AskQuestionResultView(false, result.DisplayMessage, notices);
        }

        var modelSummary = PullRequestPromptBuilder.TryExtractSummary(result.DisplayMessage, out var extracted)
            ? extracted
            : null;

        var pendingFiles = CollectPendingWriteFiles(target.RepositoryDirectory);

        if (pendingFiles.Count == 0)
        {
            _ledger.MarkProcessed(target.PullRequest, comment.Id);
            _journal.RecordProposal(pass, modelSummary, new List<PassFileChange>());
            _journal.Conclude(pass, PassState.NoChange, modelSummary);
            notices.Add($"No file change was proposed for comment {comment.Id}. It is marked processed; no reply was posted.");
            return new AskQuestionResultView(true, result.DisplayMessage, notices);
        }

        // Captured now: discarding a proposal clears the pending write it is read from.
        var changes = BuildPassChanges(target, pendingFiles);

        // All-or-nothing: a multi-file fix is never half-queued.
        var rejection = ValidateProposal(target, comment, pendingFiles);
        if (rejection is not null)
        {
            _journal.RecordProposal(
                pass, modelSummary, changes.Select(c => c with { Status = PassFileStatus.Discarded }).ToList());

            DiscardPendingWrites(pendingFiles);
            _ledger.MarkProcessed(target.PullRequest, comment.Id);
            _journal.Conclude(pass, PassState.Discarded, rejection);

            notices.Add($"Proposal for comment {comment.Id} discarded: {rejection} " +
                        "It is marked processed; no reply was posted.");
            return new AskQuestionResultView(true, result.DisplayMessage, notices);
        }

        _journal.RecordProposal(pass, modelSummary, changes);

        var paths = pendingFiles.Select(f => f.GetPendingWrite()!.AbsolutePath).ToList();
        var commitId = QueueCommit(target, comment, paths, result.DisplayMessage, remainingCandidates, notices);

        if (commitId is null)
            _journal.Conclude(pass, PassState.Discarded, "A commit for this comment was already pending; its writes remain queued.");
        else
            _journal.AttachCommit(pass, commitId);

        return new AskQuestionResultView(true, result.DisplayMessage, notices);
    }

    private AskQuestionResultView AskModel(
        SyncTarget target,
        GitHubReviewCommentResponseObject comment,
        IReadOnlyList<GitHubReviewCommentResponseObject> allComments,
        RemoteFileVersion currentFile)
    {
        var threadReplies = allComments
            .Where(c => c.InReplyToId == comment.Id && IsTrusted(c))
            .OrderBy(c => c.CreatedAt)
            .ToList();

        var question = PullRequestPromptBuilder.Build(
            target.PullRequest, comment, threadReplies, currentFile, _options.MaxFilesPerComment);

        return _repositoryIngestorService.AskQuestion(new AskRepositoryQuestionCommand
        {
            Question = question,
            RepositoryName = new RepositoryName(target.RepositoryDirectory)
        });
    }

    /// <summary>
    /// The write queue was empty before the model call, so every pending write
    /// inside the repository came from it. Sorted so the commit's file list and
    /// message are deterministic.
    /// </summary>
    private List<File> CollectPendingWriteFiles(string repositoryDirectory) =>
        _pendingWriteQueue.ListPending()
            .Where(f => f.GetPendingWrite() is { } write &&
                        PathGuard.IsWithinRoot(repositoryDirectory, write.AbsolutePath.Value))
            .OrderBy(f => f.GetPendingWrite()!.AbsolutePath.Value, StringComparer.Ordinal)
            .ToList();

    /// <summary>
    /// Returns why the model's proposal cannot be queued, or null when it can.
    /// The commented file was handed to the model fresh from the working tree.
    /// Every other file was not: the model reproduced it from the ingested
    /// snapshot, so each must still match that snapshot on disk, otherwise
    /// approving the write would silently revert newer content.
    /// </summary>
    private string? ValidateProposal(
        SyncTarget target, GitHubReviewCommentResponseObject comment, IReadOnlyList<File> files)
    {
        if (files.Count > _options.MaxFilesPerComment)
            return $"{files.Count} files were proposed; the limit is {_options.MaxFilesPerComment} " +
                   "(GitHub:MaxFilesPerComment).";

        var commentedPath = ToAbsolutePath(target, comment.Path);

        foreach (var file in files)
        {
            var write = file.GetPendingWrite()!;
            var absolutePath = Path.GetFullPath(write.AbsolutePath.Value);
            var relativePath = ToRepositoryRelative(target.RepositoryDirectory, absolutePath);

            if (relativePath.Any(char.IsControl))
                return "a proposed path contains control characters.";

            if (ContainsSymbolicLink(target.RepositoryDirectory, absolutePath))
                return $"'{relativePath}' is, or sits under, a symbolic link.";

            if (SamePath(absolutePath, commentedPath))
                continue;

            var proposedBytes = Encoding.UTF8.GetByteCount(write.ProposedContent.Value);
            if (proposedBytes > _options.MaxFileBytes)
                return $"'{relativePath}' would be {proposedBytes} bytes; the limit is {_options.MaxFileBytes}.";

            var baselineProblem = CheckBaseline(file, absolutePath, relativePath);
            if (baselineProblem is not null)
                return baselineProblem;
        }

        return null;
    }

    /// <summary>
    /// A file the model was not given must match the snapshot it worked from.
    /// A new file must not already exist on disk. Comparison ignores line
    /// endings and surrounding whitespace, because packaging can add or strip them.
    /// </summary>
    private string? CheckBaseline(File file, string absolutePath, string relativePath)
    {
        if (!System.IO.File.Exists(absolutePath))
            return file.Content.Length == 0
                ? null
                : $"'{relativePath}' no longer exists in the working tree.";

        if (new FileInfo(absolutePath).Length > _options.MaxFileBytes)
            return $"'{relativePath}' exceeds {_options.MaxFileBytes} bytes.";

        string onDisk;
        try
        {
            onDisk = System.IO.File.ReadAllText(absolutePath);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return $"'{relativePath}' could not be read: {ex.Message}";
        }

        return NormalizeForComparison(onDisk) == NormalizeForComparison(file.Content)
            ? null
            : $"'{relativePath}' differs from the ingested snapshot the model worked from. " +
              "Re-run 'ingest', then sync again.";
    }

    private static string NormalizeForComparison(string text) =>
        text.Replace("\r\n", "\n").Trim();

    private static bool SamePath(string left, string right) =>
        string.Equals(Path.GetFullPath(left), Path.GetFullPath(right),
            OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);

    /// <summary>Drops the proposal from the file and from the global queue.</summary>
    private void DiscardPendingWrites(IEnumerable<File> files)
    {
        foreach (var file in files)
        {
            var write = file.GetPendingWrite();
            if (write is not null)
                _pendingWriteQueue.TryResolve(write.AbsolutePath.Value, out _);

            file.DiscardPendingWrite();
        }
    }

    /// <summary>Returns the queued commit's id, or null when one was already pending.</summary>
    private string? QueueCommit(
        SyncTarget target,
        GitHubReviewCommentResponseObject comment,
        IReadOnlyList<LocalPath> pendingFiles,
        string modelResponse,
        int remainingCandidates,
        List<string> notices)
    {
        var summary = PullRequestPromptBuilder.TryExtractSummary(modelResponse, out var extracted)
            ? extracted
            : "Applied the requested change.";

        var relativeFiles = pendingFiles
            .Select(f => ToRepositoryRelative(target.RepositoryDirectory, f.Value))
            .ToList();

        var pending = new PendingCommit(
            new LocalPath(target.RepositoryDirectory),
            target.PullRequest,
            comment.Id,
            pendingFiles,
            new Message(PullRequestPromptBuilder.BuildCommitMessage(comment, relativeFiles)),
            target.Pull.Head.Ref,
            new Message(summary));

        var scope = relativeFiles.Count == 1
            ? $"on '{comment.Path}'"
            : $"on '{comment.Path}' covering {relativeFiles.Count} files ({string.Join(", ", relativeFiles)})";

        string? queuedId = null;

        if (_pendingCommitQueue.Enqueue(pending))
        {
            queuedId = pending.Id;
            notices.Add($"Commit queued for comment {comment.Id} {scope}. " +
                        "Run 'review': approve the write(s), then the commit.");
        }
        else
        {
            notices.Add($"A commit for comment {comment.Id} is already pending review.");
        }

        if (remainingCandidates > 0)
            notices.Add($"{remainingCandidates} further actionable comment(s). " +
                        $"Run 'pr sync {target.PullRequest.Number}' again after review.");

        return queuedId;
    }

    // ── Pass journal ─────────────────────────────────────────────────────────

    private int BeginPass(SyncTarget target, GitHubReviewCommentResponseObject comment) =>
        _journal.Begin(
            target.PullRequest,
            target.Pull.Head.Ref,
            new PassComment(comment.Id, comment.User?.Login, comment.Path, comment.Body ?? string.Empty, comment.HtmlUrl));

    private List<PassFileChange> BuildPassChanges(SyncTarget target, IEnumerable<File> files) =>
        files.Select(file =>
        {
            var write = file.GetPendingWrite()!;
            var absolutePath = Path.GetFullPath(write.AbsolutePath.Value);
            var (existedBefore, baseline) = ReadBaseline(target, absolutePath);

            return new PassFileChange(
                absolutePath,
                ToRepositoryRelative(target.RepositoryDirectory, absolutePath),
                existedBefore,
                baseline,
                write.ProposedContent.Value,
                PassFileStatus.Proposed);
        }).ToList();

    /// <summary>
    /// The file as it is on disk before any approved write. Never read through a
    /// link, and never above the size limit: the journal must not become a way
    /// to copy content from outside the repository or huge files into memory.
    /// </summary>
    private (bool ExistedBefore, string? Content) ReadBaseline(SyncTarget target, string absolutePath)
    {
        if (!System.IO.File.Exists(absolutePath))
            return (false, null);

        if (ContainsSymbolicLink(target.RepositoryDirectory, absolutePath))
            return (true, null);

        try
        {
            if (new FileInfo(absolutePath).Length > _options.MaxFileBytes)
                return (true, null);

            return (true, System.IO.File.ReadAllText(absolutePath));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return (true, null);
        }
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    private static bool SameLogin(string? left, string? right) =>
        left is not null && right is not null &&
        string.Equals(left, right, StringComparison.OrdinalIgnoreCase);

    private static bool SameSha(string left, string right) =>
        string.Equals(left, right, StringComparison.OrdinalIgnoreCase);

    // ── State carried between steps ──────────────────────────────────────────

    /// <summary>Everything resolved about the pull request being synced. Never partially filled.</summary>
    private sealed record SyncTarget(
        string RepositoryDirectory,
        string Remote,
        PullRequestReference PullRequest,
        GitHubPullRequestResponseObject Pull);

    /// <summary>All comments on the PR (for thread context) and the trusted candidates, oldest first.</summary>
    private sealed record CommentSelection(
        IReadOnlyList<GitHubReviewCommentResponseObject> AllComments,
        IReadOnlyList<GitHubReviewCommentResponseObject> Candidates);

    /// <summary>
    /// Aborts the whole sync with a user-facing message. Deliberately a private,
    /// dedicated type so SyncNextCommentAsync can catch exactly this and nothing
    /// broader — HTTP, git and programming errors must keep propagating.
    /// Messages are shown on the console as-is: never put secrets in them.
    /// </summary>
    private sealed class SyncPreconditionException : Exception
    {
        public SyncPreconditionException(string message) : base(message)
        {
        }
    }
}