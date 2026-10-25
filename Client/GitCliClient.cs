using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using SharpBastion.Interface;

namespace SharpBastion.Client;

public class GitCliClient : IGitCliClient
{
    private static readonly TimeSpan Timeout = TimeSpan.FromMinutes(2);

    private static readonly Regex RemotePattern =
        new(@"^[A-Za-z0-9][A-Za-z0-9._-]*$", RegexOptions.Compiled);

    // Characters git itself forbids in ref names, plus the ones that would change
    // the meaning of a refspec or revision we build from the name (':' rewrites
    // a refspec, '~' and '^' are revision syntax).
    private static readonly char[] ForbiddenRefCharacters = { ' ', '~', '^', ':', '?', '*', '[', '\\' };

    // Strips credentials embedded in URLs (https://user:token@host) from any
    // git output before it can reach an exception message or the console.
    private static readonly Regex UrlCredentialPattern =
        new(@"(?<=://)[^/@\s]+@", RegexOptions.Compiled);

    private static readonly StringComparison PathComparison =
        RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;

    public async Task<string> GetCurrentBranchAsync(string repositoryDirectory) =>
        (await RunAsync(repositoryDirectory, "rev-parse", "--abbrev-ref", "HEAD")).Trim();

    public async Task<string> GetRemoteUrlAsync(string repositoryDirectory, string remote)
    {
        RequireRemote(remote);
        return (await RunAsync(repositoryDirectory, "remote", "get-url", remote)).Trim();
    }

    public async Task<string> ResolveCommitAsync(string repositoryDirectory, string revision)
    {
        RequireRevision(revision);
        return (await RunAsync(repositoryDirectory, "rev-parse", "--verify", $"{revision}^{{commit}}")).Trim();
    }

    public async Task<IReadOnlyList<string>> GetChangedPathsAsync(string repositoryDirectory)
    {
        var output = await RunAsync(repositoryDirectory, "status", "--porcelain=v1", "-z", "--untracked-files=all");

        var entries = output.Split('\0', StringSplitOptions.RemoveEmptyEntries);
        var paths = new List<string>();

        for (var i = 0; i < entries.Length; i++)
        {
            var entry = entries[i];
            if (entry.Length < 4) continue; // "XY " + at least one path char

            paths.Add(entry[3..]);

            // Renames/copies carry the origin path as an extra NUL-separated entry.
            if (entry[0] is 'R' or 'C') i++;
        }

        return paths;
    }

    public async Task FetchBranchAsync(string repositoryDirectory, string remote, string branch)
    {
        RequireRemote(remote);
        RequireBranch(branch);

        await RunAsync(repositoryDirectory, "fetch", remote,
            $"+refs/heads/{branch}:refs/remotes/{remote}/{branch}");
    }

    public async Task<bool> IsAncestorAsync(
        string repositoryDirectory, string ancestorRevision, string descendantRevision)
    {
        RequireRevision(ancestorRevision);
        RequireRevision(descendantRevision);

        var result = await RunRawAsync(repositoryDirectory,
            "merge-base", "--is-ancestor", ancestorRevision, descendantRevision);

        return result.ExitCode switch
        {
            0 => true,
            1 => false,
            _ => throw new InvalidOperationException(
                $"git merge-base failed (exit {result.ExitCode}): {Redact(result.StandardError)}")
        };
    }

    public async Task StageAsync(string repositoryDirectory, IReadOnlyCollection<string> absolutePaths)
    {
        if (absolutePaths is null || absolutePaths.Count == 0)
            throw new ArgumentException("No paths to stage.", nameof(absolutePaths));

        var root = Path.GetFullPath(repositoryDirectory)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;

        var args = new List<string> { "add", "--" };
        foreach (var absolutePath in absolutePaths)
        {
            var full = Path.GetFullPath(absolutePath);
            if (!full.StartsWith(root, PathComparison))
                throw new ArgumentException($"Refusing to stage a path outside the repository: '{absolutePath}'.");

            args.Add(Path.GetRelativePath(root, full));
        }

        await RunAsync(repositoryDirectory, args.ToArray());
    }

    public async Task<string> CommitAsync(string repositoryDirectory, string message)
    {
        if (string.IsNullOrWhiteSpace(message))
            throw new ArgumentException("Commit message cannot be empty.", nameof(message));

        await RunAsync(repositoryDirectory, "commit", "-m", message);
        return (await RunAsync(repositoryDirectory, "rev-parse", "HEAD")).Trim();
    }

    public async Task PushAsync(string repositoryDirectory, string remote, string branch)
    {
        RequireRemote(remote);
        RequireBranch(branch);

        // No force flags, deliberately: a rejected non-fast-forward must surface as an error.
        await RunAsync(repositoryDirectory, "push", remote, $"HEAD:refs/heads/{branch}");
    }

    // ── Validation ───────────────────────────────────────────────────────────

    private static void RequireRemote(string remote)
    {
        if (string.IsNullOrWhiteSpace(remote) || !RemotePattern.IsMatch(remote) || remote.Contains(".."))
            throw new ArgumentException($"Invalid remote: '{remote}'.");
    }

    private static void RequireBranch(string branch)
    {
        var problem = FindBranchNameProblem(branch);
        if (problem is not null)
            throw new ArgumentException($"Invalid branch name '{branch}': {problem}.");
    }

    private static void RequireRevision(string revision)
    {
        var problem = FindRefNameProblem(revision);
        if (problem is not null)
            throw new ArgumentException($"Invalid revision '{revision}': {problem}.");
    }

    /// <summary>
    /// Rules shared by branch names and revisions. Everything else — parentheses,
    /// '#', '@', '+', unicode letters — is legal in git and is accepted: every git
    /// call here uses an argument list with no shell, so those characters are inert.
    /// Returns why the value is rejected, or null if it is acceptable.
    /// </summary>
    private static string? FindRefNameProblem(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return "it is empty";
        if (value.StartsWith('-')) return "it starts with '-'";
        if (value.Any(char.IsControl)) return "it contains a control character";

        var forbiddenIndex = value.IndexOfAny(ForbiddenRefCharacters);
        if (forbiddenIndex >= 0)
            return forbiddenIndex >= 0 && value[forbiddenIndex] == ' '
                ? "it contains a space"
                : $"it contains '{value[forbiddenIndex]}'";

        if (value.Contains("..")) return "it contains '..'";
        if (value.Contains("@{")) return "it contains '@{'";

        return null;
    }

    /// <summary>Adds git's check-ref-format rules that only apply to a branch name.</summary>
    private static string? FindBranchNameProblem(string? name)
    {
        var problem = FindRefNameProblem(name);
        if (problem is not null) return problem;

        if (name == "@") return "it is '@'";
        if (name!.StartsWith('/') || name.EndsWith('/')) return "it starts or ends with '/'";
        if (name.Contains("//")) return "it contains '//'";
        if (name.EndsWith('.')) return "it ends with '.'";

        if (name.Split('/').Any(component => component.StartsWith('.') || component.EndsWith(".lock")))
            return "a path component starts with '.' or ends with '.lock'";

        return null;
    }

    // ── Process execution ────────────────────────────────────────────────────

    private static string Redact(string text) => UrlCredentialPattern.Replace(text, "***@");

    private static async Task<string> RunAsync(string repositoryDirectory, params string[] arguments)
    {
        var result = await RunRawAsync(repositoryDirectory, arguments);
        if (result.ExitCode != 0)
            throw new InvalidOperationException(
                $"git {arguments[0]} failed (exit {result.ExitCode}): {Redact(result.StandardError).Trim()}");

        return result.StandardOutput;
    }

    private static async Task<(int ExitCode, string StandardOutput, string StandardError)> RunRawAsync(
        string repositoryDirectory, params string[] arguments)
    {
        if (!Directory.Exists(repositoryDirectory))
            throw new DirectoryNotFoundException($"Repository directory not found: '{repositoryDirectory}'.");

        var psi = new ProcessStartInfo
        {
            FileName = "git",
            WorkingDirectory = repositoryDirectory,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        foreach (var argument in arguments)
            psi.ArgumentList.Add(argument);

        // Fail fast instead of hanging on an interactive credential prompt.
        psi.Environment["GIT_TERMINAL_PROMPT"] = "0";

        using var cts = new CancellationTokenSource(Timeout);
        using var process = new Process { StartInfo = psi };
        process.Start();

        try
        {
            // Read both streams concurrently — sequential reads can deadlock on large output.
            var stdoutTask = process.StandardOutput.ReadToEndAsync(cts.Token);
            var stderrTask = process.StandardError.ReadToEndAsync(cts.Token);
            await process.WaitForExitAsync(cts.Token);
            await Task.WhenAll(stdoutTask, stderrTask);

            return (process.ExitCode, stdoutTask.Result, stderrTask.Result);
        }
        catch (OperationCanceledException)
        {
            try { process.Kill(entireProcessTree: true); } catch { /* already exited */ }
            throw new TimeoutException($"git {arguments[0]} timed out after {Timeout.TotalSeconds:0}s.");
        }
    }
}