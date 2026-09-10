using SharpBastion.Commands;
using SharpBastion.Views;

namespace SharpBastion.Interface;

public interface IScheduledJobExecutionService
{
    Task<OperationResultView> RunAsync(RunScheduledJobCommand command);
}