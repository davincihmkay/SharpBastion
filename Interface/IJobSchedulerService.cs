using SharpBastion.Domain;
using SharpBastion.ValueObjects;

namespace SharpBastion.Interface;

public interface IJobSchedulerService
{
    bool RegisterJob(ScheduledJob job);
    IReadOnlyCollection<ScheduledJob> GetAllJobs();
    ScheduledJob? GetByScriptName(ScriptName scriptName);

    bool EnqueuePendingRun(ScheduledJob job);

    IReadOnlyCollection<ScheduledJob> ListPendingRuns();
    bool TryResolvePendingRun(ScriptName scriptName, out ScheduledJob? job);
    int PendingRunCount { get; }
}