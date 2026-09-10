using SharpBastion.RequestObject;
using SharpBastion.ValueObjects;

namespace SharpBastion.Commands;

public class RunScheduledJobCommand
{
    public required ScriptName ScriptName;

    public static RunScheduledJobCommand PopulateFromRequestObject(RunJobRequestObject requestObject)
    {
        return new RunScheduledJobCommand
        {
            ScriptName = new ScriptName(requestObject.ScriptName)
        };
    }
}