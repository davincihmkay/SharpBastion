namespace SharpBastion.RequestObject;

public class ResolveCommitRequestObject
{
    public required string Id;
    public required bool Approve;

    /// <summary>
    /// Reply text typed by the user at review time. Null or blank keeps the
    /// generated reply. Ignored when the commit is rejected.
    /// </summary>
    public string? CustomReply;
}