using SharpBastion.Commands;

namespace SharpBastion.Interface;

public interface IFileWriterService
{
    Task WriteAsync(WriteFileCommand command);
}