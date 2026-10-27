using SharpBastion.Commands;
using SharpBastion.Domain;
using SharpBastion.Interface;
using SharpBastion.Views;

namespace SharpBastion.Service;

public class PendingWriteExecutionService : IPendingWriteExecutionService
{
    private readonly PendingWriteQueue _pendingWriteQueue;
    private readonly IFileWriterService _fileWriterService;
    private readonly PassJournal _passJournal;

    public PendingWriteExecutionService(
        PendingWriteQueue pendingWriteQueue,
        IFileWriterService fileWriterService,
        PassJournal passJournal)
    {
        _pendingWriteQueue = pendingWriteQueue;
        _fileWriterService = fileWriterService;
        _passJournal = passJournal;
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

        // No-op for writes that did not come from a pull request pass.
        _passJournal.RecordFileDecision(pendingWrite.AbsolutePath.Value, approved: true);

        return new OperationResultView(true, $"Written: {pendingWrite.AbsolutePath.Value}");
    }

    public OperationResultView Reject(ResolvePendingWriteCommand command)
    {
        if (!_pendingWriteQueue.TryResolve(command.Id, out var file) || file is null)
            return new OperationResultView(false, $"No pending write found for '{command.Id}'. Already resolved or unknown.");

        // Read the path before the proposal is discarded.
        var rejectedPath = file.GetPendingWrite()?.AbsolutePath.Value;

        file.DiscardPendingWrite();

        if (rejectedPath is not null)
            _passJournal.RecordFileDecision(rejectedPath, approved: false);

        return new OperationResultView(true, $"Rejected: {file.Name}");
    }
}