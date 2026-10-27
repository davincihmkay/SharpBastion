using SharpBastion.Commands;
using SharpBastion.Views;

namespace SharpBastion.Interface;

public interface IPassExportService
{
    /// <summary>
    /// Renders the recorded passes of one pull request as a self-contained HTML
    /// file. Never overwrites an existing file.
    /// </summary>
    OperationResultView Export(ExportPassesCommand command);
}