using SharpBastion.ClientRequestObject;
using SharpBastion.Commands;
using SharpBastion.Interface;
using SharpBastion.Views;

namespace SharpBastion.Service;

public class ScheduledJobExecutionService : IScheduledJobExecutionService
{
    private readonly IPythonScriptClient _pythonScriptClient;
    private readonly IJobSchedulerService _jobSchedulerService;

    public ScheduledJobExecutionService(
        IPythonScriptClient pythonScriptClient,
        IJobSchedulerService jobSchedulerService)
    {
        _pythonScriptClient = pythonScriptClient;
        _jobSchedulerService = jobSchedulerService;
    }

    public async Task<OperationResultView> RunAsync(RunScheduledJobCommand command)
    {
        var job = _jobSchedulerService.GetByScriptName(command.ScriptName);
        if (job is null)
            return new OperationResultView(false, $"Job '{command.ScriptName}' is no longer registered — skipped.");

        var request = new PythonScriptClientRunRequestObject(
            job.ScriptAbsolutePath.Value,
            Path.GetDirectoryName(job.ScriptAbsolutePath.Value)!);

        var response = await _pythonScriptClient.RunScriptAsync(request);

        return response.ExitCode == 0
            ? new OperationResultView(true, $"'{job.ScriptName}' completed (exit 0).")
            : new OperationResultView(false, $"'{job.ScriptName}' failed (exit {response.ExitCode}): {response.StandardError}");
    }
}