using SharpBastion.Commands;
using SharpBastion.Views;

namespace SharpBastion.Interface;

public interface IPendingWriteExecutionService
{
    Task<OperationResultView> ApproveAsync(ResolvePendingWriteCommand command);
    OperationResultView Reject(ResolvePendingWriteCommand command);
}