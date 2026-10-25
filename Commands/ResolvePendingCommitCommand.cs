using SharpBastion.RequestObject;

namespace SharpBastion.Commands;

public class ResolvePendingCommitCommand
{
    public required string Id;

    /// <summary>User-typed reply that replaces the generated one, or null to keep the default.</summary>
    public string? CustomReply;

    public static ResolvePendingCommitCommand PopulateFromRequestObject(ResolveCommitRequestObject requestObject)
    {
        return new ResolvePendingCommitCommand
        {
            Id = requestObject.Id,
            CustomReply = requestObject.CustomReply
        };
    }
}