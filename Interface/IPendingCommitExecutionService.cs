using SharpBastion.Commands;
using SharpBastion.Views;

namespace SharpBastion.Interface;

public interface IPendingCommitExecutionService
{
    Task<OperationResultView> ApproveAsync(ResolvePendingCommitCommand command);
    OperationResultView Reject(ResolvePendingCommitCommand command);
}