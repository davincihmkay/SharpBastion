using System.Net.Sockets;
using System.Text;
using System.Text.Json;
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
using SharpBastion.Manifest;
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

builder.Services.AddTransient<IKagiClient, KagiClient>();
builder.Services.AddSingleton<IKagiSearchProtocol, KagiSearchProtocol>();
builder.Services.AddSingleton<PendingKagiSearchQueue>();
builder.Services.AddTransient<KagiSearchController>();

builder.Services.AddTransient<IPythonScriptClient, PythonScriptClient>();
builder.Services.AddSingleton<IScriptExecutionProtocol, ScriptExecutionProtocol>();
builder.Services.AddSingleton<IJobSchedulerService, JobSchedulerService>();
builder.Services.AddTransient<IScheduledJobExecutionService, ScheduledJobExecutionService>();
builder.Services.AddSingleton<PendingJobRunQueue>();
builder.Services.AddTransient<JobController>();
builder.Services.AddHostedService<ScheduledJobRunnerHostedService>();

builder.Services.AddSingleton<ISessionListenProtocol, SessionListenProtocol>();
builder.Services.AddSingleton<IOutputBroadcaster, OutputBroadcaster>();
builder.Services.AddHostedService<SessionBroadcastServer>();

using IHost host = builder.Build();

Console.SetOut(new BroadcastingTextWriter(Console.Out, host.Services.GetRequiredService<IOutputBroadcaster>()));

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

ResolveConsent(host.Services.GetRequiredService<IExternalLlmProtocol>());
ResolveConsent(host.Services.GetRequiredService<IScriptExecutionProtocol>());
ResolveConsent(host.Services.GetRequiredService<IKagiSearchProtocol>());
ResolveConsent(host.Services.GetRequiredService<ISessionListenProtocol>());

await host.StartAsync();

RunInteractiveLoop(host.Services, assistantProfile!);

await host.StopAsync();

void ResolveConsent(IConsentProtocol protocol)
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

void RunInteractiveLoop(IServiceProvider hostProvider, AssistantProfile assistantProfile)
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
                var paths = ResolveIngestPaths(argument, homepath, assistantProfile.WorkspaceRoot);
                if (paths is null || paths.Count == 0)
                {
                    Console.WriteLine("Usage: ingest <path>[,<path>...]  (or just 'ingest' to pick from the configured workspace root)");
                    break;
                }

                Console.WriteLine($"Ingesting: {string.Join(", ", paths)}");
                try
                {
                    var result = IngestRepository(hostProvider, paths);
                    Console.WriteLine(result.Message);

                    var firstIngestedRepository = result.IngestedRepositoryPaths.FirstOrDefault();
                    if (firstIngestedRepository is not null)
                    {
                        activeRepo = firstIngestedRepository;
                        Console.WriteLine($"Active repo set to: {activeRepo}");

                        RecordSessionManifestIfListenable(hostProvider, firstIngestedRepository);
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

                    var writeQueue = hostProvider.GetRequiredService<PendingWriteQueue>();
                    if (writeQueue.Count > 0)
                        Console.WriteLine($"\n{writeQueue.Count} pending write(s) queued. Type 'review' to approve or reject.");

                    var kagiQueue = hostProvider.GetRequiredService<PendingKagiSearchQueue>();
                    if (kagiQueue.Count > 0)
                        Console.WriteLine($"{kagiQueue.Count} pending search(es) queued. Type 'review' to approve or reject.");
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

            case "listen":
            {
                try
                {
                    ListenToSession();
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
                    ReviewPendingKagiSearches(hostProvider);
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
                TryGracefulShutdown();
                Environment.Exit(0);
                break;

            default:
                Console.WriteLine($"Unknown command: '{command}'. Type 'help' for usage.");
                break;
        }
    }
}

void AbortNonInteractive()
{
    Console.WriteLine();
    Console.WriteLine("[FATAL] stdin is closed or EOF received. SharpBastion requires an interactive TTY.");
    Console.WriteLine("[HINT]  docker compose: ensure 'stdin_open: true' and 'tty: true' on the service, then run:");
    Console.WriteLine("[HINT]    docker compose run --rm sharpbastion");
    Console.WriteLine("[HINT]  docker run: pass -it, e.g. 'docker run -it <image>'");
    TryGracefulShutdown();
    Environment.Exit(1);
}

void TryGracefulShutdown()
{
    try { host.StopAsync(TimeSpan.FromSeconds(2)).GetAwaiter().GetResult(); }
    catch { /* best-effort — proceed to Environment.Exit regardless */ }
}

static string? ResolvePath(string? input, string homepath)
{
    if (input is null) return null;
    return input.StartsWith("~/")
        ? Path.Combine(homepath, input[2..])
        : input;
}

List<string>? ResolveIngestPaths(string? argument, string homepath, string workspaceRoot)
{
    if (argument is not null)
    {
        return argument.Split(',', StringSplitOptions.RemoveEmptyEntries)
            .Select(p => p.Trim())
            .Where(p => p.Length > 0)
            .Select(p => ResolvePath(p, homepath)!)
            .ToList();
    }

    var candidates = RepositoryDiscovery.ListCandidates(workspaceRoot);
    if (candidates.Count == 0)
    {
        Console.WriteLine($"No repositories found under configured workspace root: {workspaceRoot}");
        return null;
    }

    Console.WriteLine($"Repositories under {workspaceRoot}:");
    for (var i = 0; i < candidates.Count; i++) 
        Console.WriteLine($"  {i + 1}. {Path.GetFileName(candidates[i])}");

    Console.Write("Select repositories (comma-separated indices, e.g. 1,3): ");
    var rawSelection = Console.ReadLine();
    if (rawSelection is null)
    {
        AbortNonInteractive();
    }

    var selected = new List<string>();
    foreach (var token in rawSelection!.Split(',', StringSplitOptions.RemoveEmptyEntries))
    {
        if (int.TryParse(token.Trim(), out var index) && index >= 1 && index <= candidates.Count)
        {
            selected.Add(candidates[index - 1]);
        }
        else
        {
            Console.WriteLine($"[SKIP] '{token.Trim()}' is not a valid selection — ignored.");
        }
    }

    if (selected.Count == 0)
        Console.WriteLine("No repositories selected.");

    return selected;
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
    Console.WriteLine("║  listen          View live output            ║");
    Console.WriteLine("║  help            Show this menu              ║");
    Console.WriteLine("║  exit / quit     Shut down                   ║");
    Console.WriteLine(bottom);
}

static IngestRepositoriesResultView IngestRepository(IServiceProvider hostProvider, List<string> paths)
{
    using IServiceScope serviceScope = hostProvider.CreateScope();
    var controller = serviceScope.ServiceProvider.GetRequiredService<RepositoryController>();

    var request = new IngestRepositoriesRequestObject
    {
        IsBesPractice = true,
        Paths = paths
    };

    return controller.IngestRepositories(request);
}

static void RecordSessionManifestIfListenable(IServiceProvider hostProvider, string firstIngestedRepository)
{
    var sessionListenProtocol = hostProvider.GetRequiredService<ISessionListenProtocol>();
    if (!sessionListenProtocol.IsOverridden)
        return;

    var manifestPath = SessionPaths.ManifestPathFor(Environment.ProcessId);

    try
    {
        Directory.CreateDirectory(SessionPaths.SharpBastionHomeDir);

        var existing = System.IO.File.Exists(manifestPath)
            ? JsonSerializer.Deserialize<SessionManifest>(System.IO.File.ReadAllText(manifestPath))
            : null;

        if (existing?.FirstIngestedRepository is not null)
            return; // Already recorded — first ingest already won.

        var manifest = new SessionManifest { Pid = Environment.ProcessId, FirstIngestedRepository = firstIngestedRepository };
        System.IO.File.WriteAllText(manifestPath, JsonSerializer.Serialize(manifest));
    }
    catch (Exception ex)
    {
        Console.WriteLine($"[NOTICE] Could not record session manifest for 'listen' discovery: {ex.Message}");
    }
}

static AskQuestionResultView AskQuestion(IServiceProvider hostProvider, AskQuestionRequestObject resourceObject)
{
    using IServiceScope serviceScope = hostProvider.CreateScope();
    var controller = serviceScope.ServiceProvider.GetRequiredService<RepositoryController>();

    Console.WriteLine($"\nQuestion: {resourceObject.Question}");
    var result = controller.AskQuestion(resourceObject);

    if (!result.Success)
        Console.WriteLine($"[ERROR] {result.DisplayMessage}");
    else
        Console.WriteLine(result.DisplayMessage);

    foreach (var notice in result.Notices)
        Console.WriteLine($"[NOTICE] {notice}");

    return result;
}

static void ScheduleJob(IServiceProvider hostProvider, ScheduleJobRequestObject requestObject)
{
    using IServiceScope serviceScope = hostProvider.CreateScope();
    var controller = serviceScope.ServiceProvider.GetRequiredService<JobController>();

    var result = controller.ScheduleJob(requestObject);
    Console.WriteLine(result.Message);
}

void ListenToSession()
{
    var candidates = SessionDiscovery.ListCandidates();
    if (candidates.Count == 0)
    {
        Console.WriteLine($"No other running Bastion sessions found under {SessionPaths.SharpBastionHomeDir}.");
        return;
    }

    Console.WriteLine("Running Bastion sessions:");
    for (var i = 0; i < candidates.Count; i++)
    {
        var label = candidates[i].FirstIngestedRepository ?? "(no repository ingested yet)";
        Console.WriteLine($"  {i + 1}. pid {candidates[i].Pid} — {label}");
    }

    Console.Write("Select session to observe (index): ");
    var rawSelection = Console.ReadLine();
    if (rawSelection is null)
    {
        AbortNonInteractive();
    }

    if (!int.TryParse(rawSelection!.Trim(), out var index) || index < 1 || index > candidates.Count)
    {
        Console.WriteLine($"[SKIP] '{rawSelection.Trim()}' is not a valid selection — ignored.");
        return;
    }

    var target = candidates[index - 1];
    var socketPath = SessionPaths.SocketPathFor(target.Pid);

    using var socket = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
    try
    {
        socket.Connect(new UnixDomainSocketEndPoint(socketPath));
    }
    catch (Exception ex)
    {
        Console.WriteLine($"[ERROR] Could not connect to pid {target.Pid}'s session: {ex.Message}");
        return;
    }

    using var stream = new NetworkStream(socket, ownsSocket: false);
    var pumpTask = Task.Run(() => PumpSocketToConsole(stream));

    Console.WriteLine($"[ATTACHED] Streaming read-only output from pid {target.Pid}. Press Enter to stop listening.");
    Console.ReadLine(); // Any input, including a bare Enter, ends the listen session.

    try { stream.Close(); } catch { /* unblocks the pending read in PumpSocketToConsole */ }
    try { pumpTask.Wait(TimeSpan.FromSeconds(2)); } catch { /* pump task is exiting on the now-faulted read */ }

    Console.WriteLine($"[DETACHED] Stopped listening to pid {target.Pid}.");
}

static void PumpSocketToConsole(NetworkStream stream)
{
    var buffer = new byte[4096];
    try
    {
        int bytesRead;
        while ((bytesRead = stream.Read(buffer, 0, buffer.Length)) > 0)
        {
            Console.Write(Encoding.UTF8.GetString(buffer, 0, bytesRead));
        }
    }
    catch
    {
        // Stream closed — either we asked it to stop, or the remote session
        // exited first. Either way, normal end of the pump.
    }
}

void ReviewPendingWrites(IServiceProvider hostProvider)
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

void ReviewPendingJobRuns(IServiceProvider hostProvider)
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

void ReviewPendingKagiSearches(IServiceProvider hostProvider)
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
            AbortNonInteractive();
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