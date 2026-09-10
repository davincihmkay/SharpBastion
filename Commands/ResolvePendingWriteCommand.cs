using SharpBastion.RequestObject;

namespace SharpBastion.Commands;

public class ResolvePendingWriteCommand
{
    public required string Id;

    public static ResolvePendingWriteCommand PopulateFromRequestObject(ResolveWriteRequestObject requestObject)
    {
        return new ResolvePendingWriteCommand
        {
            Id = requestObject.Id
        };
    }
}