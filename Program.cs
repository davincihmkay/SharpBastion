using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SharpBastion.Client;
using SharpBastion.Controller;
using SharpBastion.Domain;
using SharpBastion.DomainService;
using SharpBastion.Helper;
using SharpBastion.Interface;
using SharpBastion.Options;
using SharpBastion.Protocol;
using SharpBastion.RequestObject;
using SharpBastion.Runner;
using SharpBastion.Service;
using SharpBastion.Views;

HostApplicationBuilder builder = Host.CreateApplicationBuilder(args);

builder.Services.AddHttpClient();
builder.Services.AddHttpClient("HealthCheck");
builder.Logging.AddFilter("System.Net.Http.HttpClient.HealthCheck", LogLevel.None);

builder.Services.Configure<AssistantOptions>(builder.Configuration.GetSection(AssistantOptions.SectionName));
builder.Services.AddSingleton(sp =>
    AssistantProfile.Load(sp.GetRequiredService<IOptions<AssistantOptions>>().Value));

builder.Services.Configure<HighlightOptions>(builder.Configuration.GetSection(HighlightOptions.SectionName));
builder.Services.AddSingleton(sp =>
    HighlightProfile.Load(sp.GetRequiredService<IOptions<HighlightOptions>>().Value));

builder.Services.AddTransient<IConsoleHighlighter>(sp =>
{
    // Coloring is a terminal-capability fact, not a consent/security decision —
    // deliberately not modeled as an IConsentProtocol. NO_COLOR (https://no-color.org)
    // and redirected output both fall back to the plain-text path unconditionally,
    // so every call site can call IConsoleHighlighter without an if-check.
    var colorCapable = !Console.IsOutputRedirected
        && Environment.GetEnvironmentVariable("NO_COLOR") is null;

    return colorCapable
        ? new ConsoleHighlighterService(sp.GetRequiredService<HighlightProfile>())
        : new NullConsoleHighlighter();
});

builder.Services.AddTransient<ILmStudioClient, LmStudioClient>();
builder.Services.AddTransient<IClaudeCliClient, ClaudeCliClient>();
builder.Services.AddTransient<IRepomixCliClient, RepomixCliClient>();
builder.Services.AddSingleton<IRepositoryIngestorService, RepositoryIngestorService>();
builder.Services.AddTransient<IRepositoryDomainService, RepositoryDomainService>();
builder.Services.AddTransient<RepositoryController>();
builder.Services.AddSingleton<PendingWriteQueue>();
builder.Services.AddSingleton<IExternalLlmProtocol, ExternalLlmProtocol>();
builder.Services.AddTransient<IFileWriterService, FileWriterService>();
builder.Services.AddTransient<IPendingWriteExecutionService, PendingWriteExecutionService>();
builder.Services.AddTransient<FileWriteController>();

builder.Services.AddTransient<IPythonScriptClient, PythonScriptClient>();
builder.Services.AddSingleton<IScriptExecutionProtocol, ScriptExecutionProtocol>();
builder.Services.AddSingleton<IJobSchedulerService, JobSchedulerService>();
builder.Services.AddTransient<IScheduledJobExecutionService, ScheduledJobExecutionService>();
builder.Services.AddSingleton<PendingJobRunQueue>();
builder.Services.AddTransient<JobController>();
builder.Services.AddHostedService<ScheduledJobRunnerHostedService>();

using IHost host = builder.Build();

// Fail fast: resolves and validates the assistant persona/model configuration
// before any consent prompts or interactive input. AssistantProfile.Load throws
// with an actionable message if the system-prompt file or model name is missing,
// unreadable, or empty — there is no embedded fallback persona.
AssistantProfile? assistantProfile = null;
try
{
    assistantProfile = host.Services.GetRequiredService<AssistantProfile>();
}
catch (InvalidOperationException ex)
{
    Console.WriteLine();
    Console.WriteLine($"[FATAL] {ex.Message}");
    Environment.Exit(1);
}

// Fail fast: same posture as AssistantProfile above — no baked-in default
// theme, so an unconfigured 'Highlight:Theme' stops startup with an
// actionable message rather than silently disabling color later.
try
{
    host.Services.GetRequiredService<HighlightProfile>();
}
catch (InvalidOperationException ex)
{
    Console.WriteLine();
    Console.WriteLine($"[FATAL] {ex.Message}");
    Environment.Exit(1);
}

ResolveConsent(host.Services.GetRequiredService<IExternalLlmProtocol>());
ResolveConsent(host.Services.GetRequiredService<IScriptExecutionProtocol>());

// Start hosted services (the scheduled-job timer) before entering the
// blocking interactive loop below. RunInteractiveLoop never returns
// (only Environment.Exit does), so the previous placement of
// `await host.RunAsync()` after it was unreachable — hosted services
// never actually started.
await host.StartAsync();

RunInteractiveLoop(host.Services, assistantProfile!);

await host.StopAsync();

static void ResolveConsent(IConsentProtocol protocol)
{
    Console.WriteLine();
    foreach (var line in protocol.Description.Split('\n'))
        Console.WriteLine($"[PROTOCOL] {line}");
    Console.Write($"[PROTOCOL] Override {protocol.Subject}? [y/n]: ");

    var rawAnswer = Console.ReadLine();
    if (rawAnswer is null)
    {
        AbortNonInteractive();
    }

    var answer = rawAnswer!.Trim().ToLower();

    if (answer == "y")
    {
        protocol.OverrideProtocol();
        Console.WriteLine($"[PROTOCOL] {protocol.OverriddenMessage}");
    }
    else
    {
        Console.WriteLine($"[PROTOCOL] {protocol.ActiveMessage}");
    }
}

static void RunInteractiveLoop(IServiceProvider hostProvider, AssistantProfile assistantProfile)
{
    var homepath = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
    string? activeRepo = null;
    var promptToken = assistantProfile.Name.ToLowerInvariant();

    PrintHelp(assistantProfile.Name);

    while (true)
    {
        Console.Write($"\n{promptToken}> ");
        var rawInput = Console.ReadLine();
        if (rawInput is null)
        {
            AbortNonInteractive();
        }

        var input = rawInput!.Trim();

        if (string.IsNullOrEmpty(input)) continue;

        var parts = input.Split(' ', 2, StringSplitOptions.RemoveEmptyEntries);
        var command = parts[0].ToLower();
        var argument = parts.Length > 1 ? parts[1] : null;

        switch (command)
        {
            case "ingest":
            {
                var path = ResolvePath(argument, homepath);
                if (path is null) { Console.WriteLine("Usage: ingest <path>  (or just 'ingest' to use ~/git/SharpBastion)"); break; }

                Console.WriteLine($"Ingesting: {path}");
                try
                {
                    var result = IngestRepository(hostProvider, path);
                    Console.WriteLine(result.Message);
                    if (result.Success)
                    {
                        activeRepo = path;
                        Console.WriteLine($"Active repo set to: {activeRepo}");
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[ERROR] {ex.Message}");
                }
                break;
            }

            case "ask":
            {
                if (argument is null) { Console.WriteLine("Usage: ask <question>"); break; }
                if (activeRepo is null) { Console.WriteLine("No active repo. Run 'ingest <path>' first."); break; }

                try
                {
                    AskQuestion(hostProvider, new AskQuestionRequestObject
                    {
                        Question = argument,
                        RepositoryName = activeRepo
                    });

                    var queue = hostProvider.GetRequiredService<PendingWriteQueue>();
                    if (queue.Count > 0)
                        Console.WriteLine($"\n{queue.Count} pending write(s) queued. Type 'review' to approve or reject.");
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[ERROR] {ex.Message}");
                }
                break;
            }

            case "use":
            {
                var path = ResolvePath(argument, homepath);
                if (path is null) { Console.WriteLine("Usage: use <path>"); break; }
                activeRepo = path;
                Console.WriteLine($"Active repo set to: {activeRepo}");
                break;
            }

            case "schedule":
            {
                if (argument is null) { Console.WriteLine("Usage: schedule <script.py> <interval>  (e.g. schedule downloadDailyBarcelonaPdf.py 24h)"); break; }

                var scheduleParts = argument.Split(' ', 2, StringSplitOptions.RemoveEmptyEntries);
                if (scheduleParts.Length < 2) { Console.WriteLine("Usage: schedule <script.py> <interval>  (e.g. schedule downloadDailyBarcelonaPdf.py 24h)"); break; }

                try
                {
                    ScheduleJob(hostProvider, new ScheduleJobRequestObject
                    {
                        ScriptName = scheduleParts[0],
                        Interval = scheduleParts[1]
                    });
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[ERROR] {ex.Message}");
                }
                break;
            }

            case "review":
            {
                try
                {
                    ReviewPendingWrites(hostProvider);
                    ReviewPendingJobRuns(hostProvider);
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[ERROR] {ex.Message}");
                }
                break;
            }

            case "status":
                Console.WriteLine(activeRepo is null ? "No active repo." : $"Active repo: {activeRepo}");
                break;

            case "help":
                PrintHelp(assistantProfile.Name);
                break;

            case "exit":
            case "quit":
                Console.WriteLine("Shutting down.");
                Environment.Exit(0);
                break;

            default:
                Console.WriteLine($"Unknown command: '{command}'. Type 'help' for usage.");
                break;
        }
    }
}

static void AbortNonInteractive()
{
    Console.WriteLine();
    Console.WriteLine("[FATAL] stdin is closed or EOF received. SharpBastion requires an interactive TTY.");
    Console.WriteLine("[HINT]  docker compose: ensure 'stdin_open: true' and 'tty: true' on the service, then run:");
    Console.WriteLine("[HINT]    docker compose run --rm sharpbastion");
    Console.WriteLine("[HINT]  docker run: pass -it, e.g. 'docker run -it <image>'");
    Environment.Exit(1);
}

static string? ResolvePath(string? input, string homepath)
{
    if (input is null) return null;
    return input.StartsWith("~/")
        ? Path.Combine(homepath, input[2..])
        : input;
}

static void PrintHelp(string name)
{
    // Box art is static; only the title is derived at runtime — from the
    // configured system-prompt file's own base name (AssistantProfile.Name),
    // never a literal here. innerWidth is computed from the border string
    // itself so the centering stays correct regardless of name length.
    const string border  = "╔══════════════════════════════════════════════╗";
    const string divider = "╠══════════════════════════════════════════════╣";
    const string bottom  = "╚══════════════════════════════════════════════╝";
    var innerWidth = border.Length - 2;

    var title = $"{name.ToUpperInvariant()} CLI";
    var leftPad = Math.Max((innerWidth - title.Length) / 2, 0);
    var rightPad = Math.Max(innerWidth - title.Length - leftPad, 0);
    var titleLine = "║" + new string(' ', leftPad) + title + new string(' ', rightPad) + "║";

    Console.WriteLine(border);
    Console.WriteLine(titleLine);
    Console.WriteLine(divider);
    Console.WriteLine("║  ingest <path>   Ingest a repository         ║");
    Console.WriteLine("║  use <path>      Switch active repo          ║");
    Console.WriteLine("║  ask <question>  Ask about active repo       ║");
    Console.WriteLine("║  schedule <s> <i> Schedule a Scripts/ job    ║");
    Console.WriteLine("║  review          Approve/reject pending items║");
    Console.WriteLine("║  status          Show active repo            ║");
    Console.WriteLine("║  help            Show this menu              ║");
    Console.WriteLine("║  exit / quit     Shut down                   ║");
    Console.WriteLine(bottom);
}

static OperationResultView IngestRepository(IServiceProvider hostProvider, string path)
{
    using IServiceScope serviceScope = hostProvider.CreateScope();
    var controller = serviceScope.ServiceProvider.GetRequiredService<RepositoryController>();

    var request = new IngestRepositoriesRequestObject
    {
        IsBesPractice = true,
        Paths = new List<string> { path }
    };

    return controller.IngestRepositories(request);
}

static AskQuestionResultView AskQuestion(IServiceProvider hostProvider, AskQuestionRequestObject resourceObject)
{
    using IServiceScope serviceScope = hostProvider.CreateScope();
    var controller = serviceScope.ServiceProvider.GetRequiredService<RepositoryController>();
    var highlighter = serviceScope.ServiceProvider.GetRequiredService<IConsoleHighlighter>();

    Console.WriteLine($"\nQuestion: {resourceObject.Question}");
    var result = controller.AskQuestion(resourceObject);

    if (!result.Success)
    {
        foreach (var segment in result.Segments.OfType<TextDisplaySegment>())
            Console.WriteLine($"[ERROR] {segment.Text}");
    }
    else
    {
        foreach (var segment in result.Segments)
        {
            switch (segment)
            {
                case TextDisplaySegment text:
                    Console.Write(text.Text);
                    break;
                case ProposalDisplaySegment proposal:
                    RenderProposalOutcome(proposal.Outcome, highlighter);
                    break;
            }
        }
        Console.WriteLine();
    }

    foreach (var notice in result.Notices)
        Console.WriteLine($"[NOTICE] {notice}");

    return result;
}

static void RenderProposalOutcome(ProposalOutcomeView outcome, IConsoleHighlighter highlighter)
{
    Console.WriteLine(outcome.HeaderLine);

    if (outcome.Status == ProposalStatus.Queued)
    {
        var languageId = outcome.LanguageId ?? "plaintext";
        Console.WriteLine(highlighter.HighlightDiff(outcome.OriginalContent!, outcome.ProposedContent!, languageId));
    }

    if (outcome.FooterLine is not null)
        Console.WriteLine(outcome.FooterLine);
}

static void ScheduleJob(IServiceProvider hostProvider, ScheduleJobRequestObject requestObject)
{
    using IServiceScope serviceScope = hostProvider.CreateScope();
    var controller = serviceScope.ServiceProvider.GetRequiredService<JobController>();

    var result = controller.ScheduleJob(requestObject);
    Console.WriteLine(result.Message);
}

static void ReviewPendingWrites(IServiceProvider hostProvider)
{
    using IServiceScope serviceScope = hostProvider.CreateScope();
    var controller = serviceScope.ServiceProvider.GetRequiredService<FileWriteController>();
    var highlighter = serviceScope.ServiceProvider.GetRequiredService<IConsoleHighlighter>();

    var pending = controller.ListPendingWrites();
    if (pending.Count == 0)
    {
        Console.WriteLine("No pending writes.");
        return;
    }

    Console.WriteLine($"{pending.Count} pending write(s).");
    foreach (var view in pending)
    {
        var languageId = LanguageMap.Resolve(view.DisplayPath);
        var coloredDiff = highlighter.HighlightDiff(view.OriginalContent, view.ProposedContent, languageId);

        Console.WriteLine($"\n  File  : {view.DisplayPath}");
        Console.WriteLine("--- diff ---");
        Console.WriteLine(coloredDiff);
        Console.WriteLine("--- end ---");
        Console.Write("Approve write? [y/n]: ");
        var rawAnswer = Console.ReadLine();
        if (rawAnswer is null)
        {
            AbortNonInteractive();
        }

        var answer = rawAnswer!.Trim().ToLower();

        Console.WriteLine(controller.ResolveWrite(new ResolveWriteRequestObject
        {
            Id = view.Id,
            Approve = answer == "y"
        }).Message);
    }
}

static void ReviewPendingJobRuns(IServiceProvider hostProvider)
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
            AbortNonInteractive();
        }

        var answer = rawAnswer!.Trim().ToLower();

        Console.WriteLine(controller.ResolveRun(new ResolveJobRunRequestObject
        {
            ScriptName = view.ScriptName,
            Approve = answer == "y"
        }).Message);
    }
}