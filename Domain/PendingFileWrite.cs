using SharpBastion.ValueObjects;

namespace SharpBastion.Domain;

public class PendingFileWrite
{
    public LocalPath AbsolutePath { get; }
    public FileContent ProposedContent { get; }

    public PendingFileWrite(LocalPath absolutePath, FileContent proposedContent)
    {
        AbsolutePath = absolutePath;
        ProposedContent = proposedContent;
    }
}