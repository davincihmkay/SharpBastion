using SharpBastion.RequestObject;

namespace SharpBastion.Commands;

public class ResolveKagiSearchCommand
{
    public required string Id;
    public required bool Approve;

    public static ResolveKagiSearchCommand PopulateFromRequestObject(ResolveKagiSearchRequestObject requestObject)
    {
        return new ResolveKagiSearchCommand
        {
            Id = requestObject.Id,
            Approve = requestObject.Approve
        };
    }
}