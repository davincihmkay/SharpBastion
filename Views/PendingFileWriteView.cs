namespace SharpBastion.Views;

public class PendingFileWriteView
{
    public readonly string Id;
    public readonly string DisplayPath;
    public readonly string Diff;

    public PendingFileWriteView(string id, string displayPath, string diff)
    {
        Id = id;
        DisplayPath = displayPath;
        Diff = diff;
    }
}