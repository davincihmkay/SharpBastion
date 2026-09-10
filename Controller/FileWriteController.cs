using SharpBastion.Commands;
using SharpBastion.Domain;
using SharpBastion.Interface;
using SharpBastion.RequestObject;
using SharpBastion.Views;

namespace SharpBastion.Controller;

public class FileWriteController
{
    private readonly PendingWriteQueue _pendingWriteQueue;
    private readonly IPendingWriteExecutionService _pendingWriteExecutionService;

    public FileWriteController(
        PendingWriteQueue pendingWriteQueue,
        IPendingWriteExecutionService pendingWriteExecutionService)
    {
        _pendingWriteQueue = pendingWriteQueue;
        _pendingWriteExecutionService = pendingWriteExecutionService;
    }

    public IReadOnlyList<PendingFileWriteView> ListPendingWrites() =>
        _pendingWriteQueue.ListPending()
            .Select(file =>
            {
                var pendingWrite = file.GetPendingWrite()!;
                return new PendingFileWriteView(
                    pendingWrite.AbsolutePath.Value,
                    file.Name,
                    file.GetPendingWriteDiff() ?? "(no changes)");
            })
            .ToList();

    public OperationResultView ResolveWrite(ResolveWriteRequestObject requestObject)
    {
        var command = ResolvePendingWriteCommand.PopulateFromRequestObject(requestObject);

        return requestObject.Approve
            ? _pendingWriteExecutionService.ApproveAsync(command).GetAwaiter().GetResult()
            : _pendingWriteExecutionService.Reject(command);
    }
}