using Microsoft.Extensions.Hosting;
using SharpBastion.Commands;
using SharpBastion.Interface;

namespace SharpBastion.Runner;

public class ScheduledJobRunnerHostedService : BackgroundService
{
    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(30);

    private readonly IJobSchedulerService _jobSchedulerService;
    private readonly IScheduledJobExecutionService _scheduledJobExecutionService;
    private readonly IScriptExecutionProtocol _scriptExecutionProtocol;

    public ScheduledJobRunnerHostedService(
        IJobSchedulerService jobSchedulerService,
        IScheduledJobExecutionService scheduledJobExecutionService,
        IScriptExecutionProtocol scriptExecutionProtocol)
    {
        _jobSchedulerService = jobSchedulerService;
        _scheduledJobExecutionService = scheduledJobExecutionService;
        _scriptExecutionProtocol = scriptExecutionProtocol;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(PollInterval);

        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            foreach (var job in _jobSchedulerService.GetAllJobs())
            {
                if (!job.IsDue) continue;

                // Advance regardless of outcome — see ScheduledJob.AdvanceNextRun.
                job.AdvanceNextRun();

                if (_scriptExecutionProtocol.IsOverridden)
                {
                    Console.WriteLine($"\n[JOB] Running '{job.ScriptName}' (unattended — script-execution protocol overridden).");
                    var result = await _scheduledJobExecutionService.RunAsync(new RunScheduledJobCommand { ScriptName = job.ScriptName });
                    Console.WriteLine($"[JOB] {result.Message}");
                }
                else
                {
                    var enqueued = _jobSchedulerService.EnqueuePendingRun(job);
                    Console.WriteLine(enqueued
                        ? $"\n[JOB] '{job.ScriptName}' is due and awaiting review. Type 'review' to approve or reject."
                        : $"\n[SKIP] job '{job.ScriptName}' already pending review.");
                }
            }
        }
    }
}