using System.Collections.Concurrent;
using SharpBastion.Domain;
using SharpBastion.Interface;
using SharpBastion.ValueObjects;

namespace SharpBastion.Service;

public class JobSchedulerService : IJobSchedulerService
{
    // No explicit comparer — ScriptName's Equals/GetHashCode already
    // handle case-insensitivity.
    private readonly ConcurrentDictionary<ScriptName, ScheduledJob> _jobs = new();

    private readonly PendingJobRunQueue _pendingJobRunQueue;

    public JobSchedulerService(PendingJobRunQueue pendingJobRunQueue)
    {
        _pendingJobRunQueue = pendingJobRunQueue;
    }

    public bool RegisterJob(ScheduledJob job) =>
        _jobs.TryAdd(job.ScriptName, job);

    public IReadOnlyCollection<ScheduledJob> GetAllJobs() =>
        _jobs.Values.ToList();

    public ScheduledJob? GetByScriptName(ScriptName scriptName) =>
        _jobs.GetValueOrDefault(scriptName);

    public bool EnqueuePendingRun(ScheduledJob job) =>
        _pendingJobRunQueue.Enqueue(job);

    public IReadOnlyCollection<ScheduledJob> ListPendingRuns() =>
        _pendingJobRunQueue.ListPending();

    public bool TryResolvePendingRun(ScriptName scriptName, out ScheduledJob? job) =>
        _pendingJobRunQueue.TryResolve(scriptName, out job);

    public int PendingRunCount =>
        _pendingJobRunQueue.Count;
}