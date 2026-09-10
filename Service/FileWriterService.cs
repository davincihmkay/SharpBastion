using SharpBastion.Commands;
using SharpBastion.Interface;

namespace SharpBastion.Service;

public class FileWriterService : IFileWriterService
{
    public async Task WriteAsync(WriteFileCommand command)
    {
        var dir = Path.GetDirectoryName(command.AbsolutePath.Value);
        if (dir is not null && !Directory.Exists(dir))
            Directory.CreateDirectory(dir);

        await System.IO.File.WriteAllTextAsync(command.AbsolutePath.Value, command.ProposedContent.Value);
    }
}