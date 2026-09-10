using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Text.Json;
using SharpBastion.ClientRequestObject;
using SharpBastion.ClientResponseObject;
using SharpBastion.Interface;

namespace SharpBastion.Client;

public class ClaudeCliClient : IClaudeCliClient
{
    private static readonly string _homepath = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

    private static readonly string _claudeCliPath =
        Environment.GetEnvironmentVariable("CLAUDE_CLI_PATH")
        ?? GetDefaultClaudePath();

    private static readonly string _sharpBastionHomeDir =
        Path.Combine(_homepath, ".sharpbastion");

    public ClaudeCliClient()
    {
        Directory.CreateDirectory(_sharpBastionHomeDir);

        var credPath = Path.Combine(_homepath, ".claude", ".credentials.json");
        var hasFileCreds = File.Exists(credPath);
        var hasKeychainCreds = RuntimeInformation.IsOSPlatform(OSPlatform.OSX)
                               && HasKeychainEntry("Claude Code-credentials");

        if (!hasFileCreds && !hasKeychainCreds)
            throw new ArgumentException(
                $"Credentials do not exist in: {_homepath}\nPlease authenticate normally using claude cli as normal");
    }

    public async Task<ClaudeCliClientChatResponseObject> SendMessageDataAsync(
        ClaudeCliClientChatPromptRequestObject clientRequestObject)
    {
        var psi = GetProcessStartInfo(
            clientRequestObject.systemPrompt,
            clientRequestObject.prompt,
            clientRequestObject.sessionId,
            clientRequestObject.workingDirectory);

        if (clientRequestObject.sessionId is not null)
        {
            psi.ArgumentList.Add("--resume");
            psi.ArgumentList.Add(clientRequestObject.sessionId);
        }

        using var cts = new CancellationTokenSource(TimeSpan.FromMinutes(15));
        using var process = new Process { StartInfo = psi };
        process.Start();

        var stdout = await process.StandardOutput.ReadToEndAsync(cts.Token);
        var stderr = await process.StandardError.ReadToEndAsync(cts.Token);
        await process.WaitForExitAsync(cts.Token);

        if (process.ExitCode != 0)
        {
            Console.WriteLine(process.ExitCode);
            throw new Exception(
                $"Claude exited with code {process.ExitCode}: \nSTDERR: {stderr} \n stdout: {stdout}");
        }

        var chatResponse = JsonSerializer.Deserialize<ClaudeCliClientChatResponseObject>(stdout);
        if (chatResponse is null)
            throw new ArgumentException("No response from chat, exception occurred");

        return chatResponse;
    }

    private static ProcessStartInfo GetProcessStartInfo(
        string systemPrompt,
        string prompt,
        string? sessionId,
        string workingDirectory)
    {
        Directory.CreateDirectory(workingDirectory);

        var psi = new ProcessStartInfo
        {
            FileName = _claudeCliPath,
            WorkingDirectory = workingDirectory,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };

        psi.ArgumentList.Add("--print");
        psi.ArgumentList.Add(prompt);
        psi.ArgumentList.Add("--system-prompt");
        psi.ArgumentList.Add(systemPrompt);
        psi.ArgumentList.Add("--output-format");
        psi.ArgumentList.Add("json");
        psi.ArgumentList.Add("--allowedTools");
        psi.ArgumentList.Add("Read"); // Read only — allows file access, blocks all write-side tools

        return psi;
    }

    public void CleanupSession(string sessionId)
    {
        var workDir = Path.Combine(_sharpBastionHomeDir, sessionId);
        if (Directory.Exists(workDir))
            Directory.Delete(workDir, recursive: true);
    }

    private static string GetDefaultClaudePath()
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            return "claude";

        return Path.Combine(_homepath, ".local", "bin", "claude");
    }

    [SupportedOSPlatform("osx")]
    [DllImport("/System/Library/Frameworks/Security.framework/Security")]
    private static extern int SecKeychainFindGenericPassword(
        IntPtr keychainOrArray,
        uint serviceNameLength, string serviceName,
        uint accountNameLength, string? accountName,
        out uint passwordLength, out IntPtr passwordData,
        IntPtr itemRef);

    [SupportedOSPlatform("osx")]
    private static bool HasKeychainEntry(string service) =>
        SecKeychainFindGenericPassword(
            IntPtr.Zero,
            (uint)service.Length, service,
            0, null,
            out _, out _, IntPtr.Zero) == 0;
}