using System.Diagnostics;
using System.Xml.Serialization;
using SharpBastion.ClientRequestObject;
using SharpBastion.ClientResponseObject;
using SharpBastion.Interface;

namespace SharpBastion.Client;

public class RepomixCliClient : IRepomixCliClient
{
    private static readonly string _homepath =
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

    private static readonly string _gitPath =
        Path.Combine(_homepath, "git");

    private static readonly string _sharpBastionHomeDir =
        Path.Combine(_homepath, ".sharpbastion");

    public RepomixCliClient()
    {
        Directory.CreateDirectory(_sharpBastionHomeDir);
    }

    public async Task<RepomixCliClientPackageRepositoryResponseObject> PackageRepositoryToXml(
        RepomixCliClientPackageRepositoryRequestObject clientRequestObject)
    {
        var serializer = new XmlSerializer(typeof(RepomixCliClientPackageRepositoryResponseObject));

        var repoSlug = Path.GetFileName(clientRequestObject.repositoryName.TrimEnd('/', '\\'));
        var sessionDir = Path.Combine(_sharpBastionHomeDir, Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(sessionDir);

        var xmlFileName = $"{repoSlug}.xml";
        var tempFile = Path.Combine(sessionDir, xmlFileName);

        var psi = GetProcessStartInfo(clientRequestObject.repositoryName, tempFile);

        using var cts = new CancellationTokenSource(TimeSpan.FromMinutes(5));
        using var process = new Process { StartInfo = psi };
        process.Start();

        var stderr = await process.StandardError.ReadToEndAsync(cts.Token);
        await process.WaitForExitAsync(cts.Token);

        if (process.ExitCode != 0)
        {
            Console.WriteLine(process.ExitCode);
            throw new Exception(
                $"Repomix exited with code {process.ExitCode}: \nSTDERR: {stderr}");
        }

        var rawXml = await System.IO.File.ReadAllTextAsync(tempFile, cts.Token);
        using var reader = new StringReader(rawXml);
        var result = (RepomixCliClientPackageRepositoryResponseObject)serializer.Deserialize(reader)!;

        if (result is null)
            throw new ArgumentException("No result from Repomix, exception occurred");

        // File is intentionally left on disk — ClaudeCliClient reads it
        // from sessionDir as its working directory.
        result.TempDirectory = sessionDir;
        result.XmlFileName = xmlFileName;

        return result;
    }

    private static ProcessStartInfo GetProcessStartInfo(string path, string outputFile)
    {
        var psi = new ProcessStartInfo
        {
            FileName = "repomix",
            WorkingDirectory = _gitPath,
            UseShellExecute = false,
            RedirectStandardOutput = false,
            RedirectStandardError = true,
        };

        psi.ArgumentList.Add(path);
        psi.ArgumentList.Add("--output");
        psi.ArgumentList.Add(outputFile);
        psi.ArgumentList.Add("--parsable-style");
        return psi;
    }

    public void CleanupSession(string sessionId)
    {
        var workDir = Path.Combine(_sharpBastionHomeDir, sessionId);
        if (Directory.Exists(workDir))
            Directory.Delete(workDir, recursive: true);
    }

    public bool IsInitialized() => true;
}