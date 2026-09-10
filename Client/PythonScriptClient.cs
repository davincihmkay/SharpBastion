using System.Diagnostics;
using System.Runtime.InteropServices;
using SharpBastion.ClientRequestObject;
using SharpBastion.ClientResponseObject;
using SharpBastion.Interface;

namespace SharpBastion.Client;

public class PythonScriptClient : IPythonScriptClient
{
    private static readonly string _pythonBinary =
        RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? "python" : "python3";

    private static readonly TimeSpan _timeout = TimeSpan.FromMinutes(5);

    public async Task<PythonScriptClientRunResponseObject> RunScriptAsync(
        PythonScriptClientRunRequestObject clientRequestObject)
    {
        var psi = new ProcessStartInfo
        {
            FileName = _pythonBinary,
            WorkingDirectory = clientRequestObject.workingDirectory,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        psi.ArgumentList.Add(clientRequestObject.scriptPath);

        using var cts = new CancellationTokenSource(_timeout);
        using var process = new Process { StartInfo = psi };
        process.Start();

        var stdout = await process.StandardOutput.ReadToEndAsync(cts.Token);
        var stderr = await process.StandardError.ReadToEndAsync(cts.Token);
        await process.WaitForExitAsync(cts.Token);

        // Unlike ClaudeCliClient/RepomixCliClient, a non-zero exit does not
        // throw here — job execution is a queued/background flow, and the
        // caller (review command, hosted scheduler) decides how to surface
        // a failed run rather than the client raising for it.
        return new PythonScriptClientRunResponseObject
        {
            StandardOutput = stdout,
            StandardError = stderr,
            ExitCode = process.ExitCode
        };
    }
}