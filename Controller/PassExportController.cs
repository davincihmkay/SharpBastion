using SharpBastion.Commands;
using SharpBastion.Interface;
using SharpBastion.RequestObject;
using SharpBastion.Views;

namespace SharpBastion.Controller;

public class PassExportController
{
    private readonly IPassExportService _passExportService;

    public PassExportController(IPassExportService passExportService)
    {
        _passExportService = passExportService;
    }

    public OperationResultView Export(ExportPassesRequestObject requestObject)
    {
        var command = ExportPassesCommand.PopulateFromRequestObject(requestObject);
        return _passExportService.Export(command);
    }
}