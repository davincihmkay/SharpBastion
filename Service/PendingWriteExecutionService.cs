using SharpBastion.Commands;
using SharpBastion.Domain;
using SharpBastion.Interface;
using SharpBastion.Views;

namespace SharpBastion.Service;

public class PendingWriteExecutionService : IPendingWriteExecutionService
{
    private readonly PendingWriteQueue _pendingWriteQueue;
    private readonly IFileWriterService _fileWriterService;

    public PendingWriteExecutionService(
        PendingWriteQueue pendingWriteQueue,
        IFileWriterService fileWriterService)
    {
        _pendingWriteQueue = pendingWriteQueue;
        _fileWriterService = fileWriterService;
    }

    public async Task<OperationResultView> ApproveAsync(ResolvePendingWriteCommand command)
    {
        if (!_pendingWriteQueue.TryResolve(command.Id, out var file) || file is null)
            return new OperationResultView(false, $"No pending write found for '{command.Id}'. Already resolved or unknown.");

        var pendingWrite = file.GetPendingWrite();
        if (pendingWrite is null)
            return new OperationResultView(false, $"'{command.Id}' has no pending write payload — inconsistent state.");

        await _fileWriterService.WriteAsync(new WriteFileCommand
        {
            AbsolutePath = pendingWrite.AbsolutePath,
            ProposedContent = pendingWrite.ProposedContent
        });

        file.AcceptPendingWrite();

        return new OperationResultView(true, $"Written: {pendingWrite.AbsolutePath.Value}");
    }

    public OperationResultView Reject(ResolvePendingWriteCommand command)
    {
        if (!_pendingWriteQueue.TryResolve(command.Id, out var file) || file is null)
            return new OperationResultView(false, $"No pending write found for '{command.Id}'. Already resolved or unknown.");

        file.DiscardPendingWrite();

        return new OperationResultView(true, $"Rejected: {file.Name}");
    }
}