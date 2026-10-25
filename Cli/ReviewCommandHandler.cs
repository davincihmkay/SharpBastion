using Microsoft.Extensions.DependencyInjection;
using SharpBastion.Controller;
using SharpBastion.Domain;
using SharpBastion.Interface;
using SharpBastion.RequestObject;

namespace SharpBastion.Cli;

/// <summary>
/// Console command handler for 'review': interactively approves or rejects
/// every kind of pending item. Also used by 'pr sync --all', so both go
/// through exactly the same prompts. The host's non-interactive abort is
/// passed in.
/// </summary>
public static class ReviewCommandHandler
{
    /// <summary>Every review section, in order. Commits come last so the writes they depend on are resolved first.</summary>
    public static void ReviewAll(IServiceProvider hostProvider, Action abortNonInteractive)
    {
        ReviewPendingWrites(hostProvider, abortNonInteractive);
        ReviewPendingJobRuns(hostProvider, abortNonInteractive);
        ReviewPendingKagiSearches(hostProvider, abortNonInteractive);
        ReviewPendingCommits(hostProvider, abortNonInteractive);
    }

    public static void ReviewPendingWrites(IServiceProvider hostProvider, Action abortNonInteractive)
    {
        using IServiceScope serviceScope = hostProvider.CreateScope();
        var controller = serviceScope.ServiceProvider.GetRequiredService<FileWriteController>();

        var pending = controller.ListPendingWrites();
        if (pending.Count == 0)
        {
            Console.WriteLine("No pending writes.");
            return;
        }

        Console.WriteLine($"{pending.Count} pending write(s).");
        foreach (var view in pending)
        {
            Console.WriteLine($"\n  File  : {view.DisplayPath}");
            Console.WriteLine("--- diff ---");
            Console.WriteLine(view.Diff);
            Console.WriteLine("--- end ---");
            Console.Write("Approve write? [y/n]: ");
            var rawAnswer = Console.ReadLine();
            if (rawAnswer is null)
            {
                abortNonInteractive();
            }

            var answer = rawAnswer!.Trim().ToLower();

            Console.WriteLine(controller.ResolveWrite(new ResolveWriteRequestObject
            {
                Id = view.Id,
                Approve = answer == "y"
            }).Message);
        }
    }

    public static void ReviewPendingJobRuns(IServiceProvider hostProvider, Action abortNonInteractive)
    {
        using IServiceScope serviceScope = hostProvider.CreateScope();
        var controller = serviceScope.ServiceProvider.GetRequiredService<JobController>();

        var pending = controller.ListPendingRuns();
        if (pending.Count == 0)
        {
            Console.WriteLine("No pending job runs.");
            return;
        }

        Console.WriteLine($"{pending.Count} pending job run(s).");
        foreach (var view in pending)
        {
            Console.WriteLine($"\n  Job   : {view.ScriptName}");
            Console.WriteLine($"  Path  : {view.ScriptAbsolutePath}");
            Console.WriteLine($"  Every : {view.Interval}");
            Console.Write("Run now? [y/n]: ");
            var rawAnswer = Console.ReadLine();
            if (rawAnswer is null)
            {
                abortNonInteractive();
            }

            var answer = rawAnswer!.Trim().ToLower();

            Console.WriteLine(controller.ResolveRun(new ResolveJobRunRequestObject
            {
                ScriptName = view.ScriptName,
                Approve = answer == "y"
            }).Message);
        }
    }

    public static void ReviewPendingKagiSearches(IServiceProvider hostProvider, Action abortNonInteractive)
    {
        using IServiceScope serviceScope = hostProvider.CreateScope();
        var controller = serviceScope.ServiceProvider.GetRequiredService<KagiSearchController>();

        var pending = controller.ListPendingSearches();
        if (pending.Count == 0)
        {
            Console.WriteLine("No pending searches.");
            return;
        }

        Console.WriteLine($"{pending.Count} pending search(es).");
        foreach (var view in pending)
        {
            Console.WriteLine($"\n  Query : {view.Query}");
            Console.Write("Approve search? [y/n]: ");
            var rawAnswer = Console.ReadLine();
            if (rawAnswer is null)
            {
                abortNonInteractive();
            }

            var answer = rawAnswer!.Trim().ToLower();

            var result = controller.ResolveSearch(new ResolveKagiSearchRequestObject
            {
                Id = view.Id,
                Approve = answer == "y"
            });

            if (!result.Success)
                Console.WriteLine($"[ERROR] {result.DisplayMessage}");
            else
                Console.WriteLine(result.DisplayMessage);

            foreach (var notice in result.Notices)
                Console.WriteLine($"[NOTICE] {notice}");
        }
    }

    public static void ReviewPendingCommits(IServiceProvider hostProvider, Action abortNonInteractive)
    {
        // Singleton, no dependencies — checked first so 'review' never needs
        // GITHUB_TOKEN unless a commit is actually pending.
        var queue = hostProvider.GetRequiredService<PendingCommitQueue>();
        if (queue.Count == 0)
        {
            Console.WriteLine("No pending commits.");
            return;
        }

        using var scope = hostProvider.CreateScope();
        var controller = scope.ServiceProvider.GetRequiredService<PullRequestController>();
        var protocol = hostProvider.GetRequiredService<IGitHubProtocol>();

        var pending = controller.ListPendingCommits();
        Console.WriteLine($"{pending.Count} pending commit(s).");

        foreach (var view in pending)
        {
            Console.WriteLine($"\n  PR      : {view.PullRequest} (comment {view.ReviewCommentId})");
            Console.WriteLine($"  Branch  : {view.Branch}");
            Console.WriteLine($"  Files   : {string.Join(", ", view.Files)}");
            Console.WriteLine($"  Message : {view.CommitMessage.Split('\n')[0]}");
            Console.WriteLine($"  Reply   : {view.ReplyBody}");
            Console.WriteLine("            (default reply; the commit SHA and an automated-response note are added when posted)");

            bool approve;
            string? customReply = null;

            if (protocol.IsOverridden)
            {
                Console.WriteLine("[GITHUB] Committing without a prompt (github protocol overridden).");
                approve = true;
            }
            else
            {
                Console.Write("Commit, push and reply? [y/n]: ");
                var rawAnswer = Console.ReadLine();
                if (rawAnswer is null)
                {
                    abortNonInteractive();
                }

                approve = rawAnswer!.Trim().ToLower() == "y";

                if (approve)
                {
                    Console.Write("Custom reply (Enter to keep the default): ");
                    var rawReply = Console.ReadLine();
                    if (rawReply is null)
                    {
                        abortNonInteractive();
                    }

                    customReply = string.IsNullOrWhiteSpace(rawReply) ? null : rawReply.Trim();
                }
            }

            var result = controller.ResolveCommit(new ResolveCommitRequestObject
            {
                Id = view.Id,
                Approve = approve,
                CustomReply = customReply
            });

            Console.WriteLine(result.Success ? result.Message : $"[ERROR] {result.Message}");
        }
    }
}