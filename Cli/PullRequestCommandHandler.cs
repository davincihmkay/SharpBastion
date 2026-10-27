using Microsoft.Extensions.DependencyInjection;
using SharpBastion.Controller;
using SharpBastion.Domain;
using SharpBastion.RequestObject;

namespace SharpBastion.Cli;

/// <summary>
/// Console command handler for 'pr'. Reviewing what a sync queued is done by
/// ReviewCommandHandler; the host's non-interactive abort is passed in.
/// </summary>
public static class PullRequestCommandHandler
{
    private const int MaxChainedIterations = 10;
    private const string Usage = "Usage: pr sync <number> [--all] [--export]";

    /// <summary>
    /// 'pr sync &lt;n&gt;' handles one comment. 'pr sync &lt;n&gt; --all' chains
    /// sync and review until nothing is left, a sync fails, or the iteration
    /// cap is hit. Every write and commit is still approved interactively.
    /// '--export' writes an HTML report of what a sync queued, before it is
    /// reviewed: once for a single sync, once per iteration when chained.
    /// </summary>
    public static void Run(IServiceProvider hostProvider, string activeRepo, string? argument, Action abortNonInteractive)
    {
        if (!TryParseSyncArguments(argument, out var number, out var chained, out var export))
        {
            Console.WriteLine(Usage);
            return;
        }

        if (chained)
        {
            RunChained(hostProvider, activeRepo, number, export, abortNonInteractive);
            return;
        }

        if (SyncOnce(hostProvider, activeRepo, number) && export)
            ExportPendingIfQueued(hostProvider, number);

        PrintQueueSummary(hostProvider);
    }

    private static bool TryParseSyncArguments(string? argument, out int number, out bool chained, out bool export)
    {
        number = 0;
        chained = false;
        export = false;

        var parts = (argument ?? string.Empty).Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length is < 2 or > 4) return false;
        if (!parts[0].Equals("sync", StringComparison.OrdinalIgnoreCase)) return false;
        if (!int.TryParse(parts[1], out number) || number <= 0) return false;

        foreach (var flag in parts.Skip(2))
        {
            if (flag.Equals("--all", StringComparison.OrdinalIgnoreCase) && !chained)
                chained = true;
            else if (flag.Equals("--export", StringComparison.OrdinalIgnoreCase) && !export)
                export = true;
            else
                return false;
        }

        return true;
    }

    private static void RunChained(
        IServiceProvider hostProvider, string activeRepo, int number, bool export, Action abortNonInteractive)
    {
        var writeQueue = hostProvider.GetRequiredService<PendingWriteQueue>();
        var commitQueue = hostProvider.GetRequiredService<PendingCommitQueue>();
        var ledger = hostProvider.GetRequiredService<ProcessedReviewCommentLedger>();

        var reviewed = 0;
        var stopReason = $"iteration cap ({MaxChainedIterations}) reached";

        for (var iteration = 1; iteration <= MaxChainedIterations; iteration++)
        {
            Console.WriteLine($"\n[PR] Chained sync {iteration}/{MaxChainedIterations} for #{number}");
            var processedBefore = ledger.Count;

            if (!SyncOnce(hostProvider, activeRepo, number))
            {
                stopReason = "sync did not succeed";
                break;
            }

            // Something was proposed: report it if asked, review it with the normal prompts, then go again.
            if (commitQueue.Count > 0 || writeQueue.Count > 0)
            {
                if (export)
                    ExportPendingIfQueued(hostProvider, number);

                ReviewCommandHandler.ReviewAll(hostProvider, abortNonInteractive);
                reviewed++;
                continue;
            }

            // Nothing queued, but a comment was skipped or needed no change — keep going.
            if (ledger.Count > processedBefore)
                continue;

            stopReason = "no further actionable comments";
            break;
        }

        Console.WriteLine($"\n[PR] Chain finished: {reviewed} comment(s) reviewed. Stopped: {stopReason}.");
    }

    /// <summary>Runs one sync, prints its outcome, and returns whether it succeeded.</summary>
    private static bool SyncOnce(IServiceProvider hostProvider, string activeRepo, int number)
    {
        using var scope = hostProvider.CreateScope();
        var controller = scope.ServiceProvider.GetRequiredService<PullRequestController>();

        Console.WriteLine($"\nSyncing pull request #{number} for {activeRepo}");
        var result = controller.SyncNextComment(new SyncPullRequestRequestObject
        {
            RepositoryPath = activeRepo,
            PullRequestNumber = number
        });

        Console.WriteLine(result.Success ? result.DisplayMessage : $"[ERROR] {result.DisplayMessage}");
        foreach (var notice in result.Notices)
            Console.WriteLine($"[NOTICE] {notice}");

        return result.Success;
    }

    /// <summary>
    /// A pass is awaiting review exactly while its commit is queued, so an empty
    /// commit queue means there is nothing to report. Best effort: a failed
    /// export is printed and never stops the sync or the review that follows.
    /// </summary>
    private static void ExportPendingIfQueued(IServiceProvider hostProvider, int number)
    {
        var commitQueue = hostProvider.GetRequiredService<PendingCommitQueue>();
        if (commitQueue.Count == 0) return;

        try
        {
            using var scope = hostProvider.CreateScope();
            var controller = scope.ServiceProvider.GetRequiredService<PassExportController>();

            var result = controller.Export(new ExportPassesRequestObject
            {
                PullRequestNumber = number,
                PendingOnly = true
            });

            Console.WriteLine(result.Success ? $"[EXPORT] {result.Message}" : $"[EXPORT ERROR] {result.Message}");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[EXPORT ERROR] {ex.Message}");
        }
    }

    private static void PrintQueueSummary(IServiceProvider hostProvider)
    {
        var writeQueue = hostProvider.GetRequiredService<PendingWriteQueue>();
        if (writeQueue.Count > 0)
            Console.WriteLine($"\n{writeQueue.Count} pending write(s) queued. Type 'review' to approve or reject.");

        var commitQueue = hostProvider.GetRequiredService<PendingCommitQueue>();
        if (commitQueue.Count > 0)
            Console.WriteLine($"{commitQueue.Count} pending commit(s) queued. Type 'review' to approve or reject.");
    }
}