using SharpBastion.RequestObject;
using SharpBastion.ValueObjects;

namespace SharpBastion.Commands;

public class ResolveJobRunCommand
{
    public required ScriptName ScriptName;
    public required bool Approve;

    public static ResolveJobRunCommand PopulateFromRequestObject(ResolveJobRunRequestObject requestObject)
    {
        return new ResolveJobRunCommand
        {
            ScriptName = new ScriptName(requestObject.ScriptName),
            Approve = requestObject.Approve
        };
    }
}