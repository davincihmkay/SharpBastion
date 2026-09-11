namespace SharpBastion.Views;

public class PendingFileWriteView
{
    public readonly string Id;
    public readonly string DisplayPath;
    public readonly string OriginalContent;
    public readonly string ProposedContent;

    public PendingFileWriteView(
        string id,
        string displayPath,
        string originalContent,
        string proposedContent)
    {
        Id = id;
        DisplayPath = displayPath;
        OriginalContent = originalContent;
        ProposedContent = proposedContent;
    }
}