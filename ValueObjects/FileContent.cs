namespace SharpBastion.ValueObjects;

public class FileContent
{
    public readonly string Value;

    public FileContent(string content)
    {
        Value = content;
    }
}