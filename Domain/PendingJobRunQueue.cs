using System.Collections.Concurrent;
using SharpBastion.ValueObjects;

namespace SharpBastion.Domain;

public class PendingJobRunQueue
{
    // No explicit comparer — ScriptName's Equals/GetHashCode already
    // handle case-insensitivity.
    private readonly ConcurrentDictionary<ScriptName, ScheduledJob> _pending = new();

    public bool Enqueue(ScheduledJob job) => _pending.TryAdd(job.ScriptName, job);

    public IReadOnlyCollection<ScheduledJob> ListPending() => _pending.Values.ToList();

    public bool TryResolve(ScriptName scriptName, out ScheduledJob? job) => _pending.TryRemove(scriptName, out job);

    public int Count => _pending.Count;
}