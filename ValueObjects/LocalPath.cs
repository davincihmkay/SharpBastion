namespace SharpBastion.ValueObjects;

public class LocalPath
{
    public readonly string Value;
    
    public LocalPath(string path)
    {
        Value = Path.GetFullPath(path);
    }
}