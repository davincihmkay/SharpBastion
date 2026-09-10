using SharpBastion.Commands;
using SharpBastion.Interface;
using SharpBastion.RequestObject;
using SharpBastion.Views;

namespace SharpBastion.Controller;

public class JobController
{
    private readonly IJobSchedulerService _jobSchedulerService;
    private readonly IScheduledJobExecutionService _scheduledJobExecutionService;

    public JobController(
        IJobSchedulerService jobSchedulerService,
        IScheduledJobExecutionService scheduledJobExecutionService)
    {
        _jobSchedulerService = jobSchedulerService;
        _scheduledJobExecutionService = scheduledJobExecutionService;
    }

    public OperationResultView ScheduleJob(ScheduleJobRequestObject requestObject)
    {
        var command = ScheduleJobCommand.PopulateFromRequestObject(requestObject);
        var registered = _jobSchedulerService.RegisterJob(command.Job);

        return registered
            ? new OperationResultView(true, $"Scheduled '{command.Job.ScriptName}' every {command.Job.Interval}.")
            : new OperationResultView(false, $"A job for '{command.Job.ScriptName}' is already scheduled. Rejected duplicate.");
    }

    public IReadOnlyList<PendingJobRunView> ListPendingRuns() =>
        _jobSchedulerService.ListPendingRuns()
            .Select(job => new PendingJobRunView(job.ScriptName.Value, job.ScriptAbsolutePath.Value, job.Interval))
            .ToList();

    public OperationResultView ResolveRun(ResolveJobRunRequestObject requestObject)
    {
        var command = ResolveJobRunCommand.PopulateFromRequestObject(requestObject);

        if (!_jobSchedulerService.TryResolvePendingRun(command.ScriptName, out var job) || job is null)
            return new OperationResultView(false, $"No pending run found for '{command.ScriptName}'. Already resolved or unknown.");

        return command.Approve
            ? RunJob(new RunJobRequestObject { ScriptName = command.ScriptName.Value })
            : new OperationResultView(true, $"Rejected: {job.ScriptName}");
    }

    public OperationResultView RunJob(RunJobRequestObject requestObject)
    {
        var command = RunScheduledJobCommand.PopulateFromRequestObject(requestObject);
        return _scheduledJobExecutionService.RunAsync(command).GetAwaiter().GetResult();
    }
}