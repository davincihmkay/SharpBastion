using SharpBastion.Domain;
using SharpBastion.Helper;
using SharpBastion.RequestObject;
using SharpBastion.ValueObjects;

namespace SharpBastion.Commands;

public class ScheduleJobCommand
{
    public required ScheduledJob Job;

    public static ScheduleJobCommand PopulateFromRequestObject(ScheduleJobRequestObject requestObject)
    {
        if (!IntervalParser.TryParse(requestObject.Interval, out var interval))
            throw new ArgumentException(
                $"Unrecognized interval '{requestObject.Interval}'. Use formats like '30m', '24h', '2d'.");

        return new ScheduleJobCommand
        {
            Job = new ScheduledJob(new ScriptName(requestObject.ScriptName), interval)
        };
    }
}