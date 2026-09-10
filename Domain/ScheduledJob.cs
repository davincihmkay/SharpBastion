using SharpBastion.Helper;
using SharpBastion.ValueObjects;

namespace SharpBastion.Domain;

public class ScheduledJob
{
    private static readonly string _scriptsRoot =
        Path.Combine(Directory.GetCurrentDirectory(), "Scripts");

    public ScriptName ScriptName { get; }
    public LocalPath ScriptAbsolutePath { get; }
    public TimeSpan Interval { get; }
    public DateTime NextRunUtc { get; private set; }

    public ScheduledJob(ScriptName scriptName, TimeSpan interval)
    {
        var candidate = new LocalPath(Path.Combine(_scriptsRoot, scriptName.Value));

        if (!PathGuard.IsWithinRoot(_scriptsRoot, candidate.Value))
            throw new ArgumentException($"'{scriptName.Value}' resolves outside the Scripts/ directory. Rejected.");

        if (!System.IO.File.Exists(candidate.Value))
            throw new ArgumentException($"Script not found: {candidate.Value}");

        ScriptName = scriptName;
        ScriptAbsolutePath = candidate;
        Interval = interval;
        NextRunUtc = DateTime.UtcNow.Add(interval);
    }

    public bool IsDue => DateTime.UtcNow >= NextRunUtc;

    /// <summary>
    /// Advances from the prior NextRunUtc (not "now") to avoid cadence
    /// drift from execution/approval delay — runs regardless of whether
    /// the due occurrence was executed or only queued for review.
    /// </summary>
    public void AdvanceNextRun() => NextRunUtc = NextRunUtc.Add(Interval);
}