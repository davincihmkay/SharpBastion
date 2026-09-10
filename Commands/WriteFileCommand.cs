using SharpBastion.ValueObjects;

namespace SharpBastion.Commands;

public class WriteFileCommand
{
    public required LocalPath AbsolutePath;
    public required FileContent ProposedContent;
}
