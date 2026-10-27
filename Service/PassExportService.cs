using System.Globalization;
using System.Text;
using SharpBastion.Commands;
using SharpBastion.Domain;
using SharpBastion.Helper;
using SharpBastion.Interface;
using SharpBastion.ValueObjects;
using SharpBastion.Views;

namespace SharpBastion.Service;

public class PassExportService : IPassExportService
{
    private readonly PassJournal _journal;

    public PassExportService(PassJournal journal)
    {
        _journal = journal;
    }

    public OperationResultView Export(ExportPassesCommand command)
    {
        var all = _journal.List();
        if (all.Count == 0)
            return new OperationResultView(false, "No passes recorded in this session. Run 'pr sync' first.");

        var selected = command.PullRequestNumber is { } number
            ? all.Where(p => p.PullRequest.Number == number).ToList()
            : all.ToList();

        if (selected.Count == 0)
            return new OperationResultView(false, $"No passes recorded for pull request #{command.PullRequestNumber}.");

        var pullRequests = selected.Select(p => p.PullRequest).Distinct().ToList();
        if (pullRequests.Count > 1)
        {
            var list = string.Join(", ", pullRequests);

            return command.PullRequestNumber is null
                ? new OperationResultView(false,
                    $"Passes exist for {pullRequests.Count} pull requests: {list}. Re-run with 'export --pr <number>'.")
                : new OperationResultView(false,
                    $"Pull request number {command.PullRequestNumber} matches several repositories: {list}. " +
                    "Exporting by number alone cannot tell them apart.");
        }

        var pullRequest = pullRequests[0];
        var html = PassReportRenderer.Render(selected, DateTimeOffset.UtcNow);

        string path;
        try
        {
            path = ResolveOutputPath(command, pullRequest);
        }
        catch (ArgumentException ex)
        {
            return new OperationResultView(false, ex.Message);
        }

        try
        {
            WriteNew(path, html);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            var hint = File.Exists(path) ? " The file already exists; choose another --out or delete it." : string.Empty;
            return new OperationResultView(false, $"Could not write '{path}': {ex.Message}{hint}");
        }

        return new OperationResultView(true,
            $"Exported {selected.Count} pass(es) for {pullRequest} to {path}. " +
            "It contains source code and review comments: treat it as sensitive.");
    }

    private static string ResolveOutputPath(ExportPassesCommand command, PullRequestReference pullRequest)
    {
        if (command.OutputPath is not null)
        {
            var custom = Path.GetFullPath(command.OutputPath);
            var parent = Path.GetDirectoryName(custom);

            if (parent is null || !Directory.Exists(parent))
                throw new ArgumentException($"Directory '{parent}' does not exist.");

            return custom;
        }

        var directory = Path.Combine(SessionPaths.SharpBastionHomeDir, "exports");
        EnsurePrivateDirectory(directory);

        // Owner and repository are validated by PullRequestReference, so they are safe in a file name.
        var stamp = DateTime.UtcNow.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture);
        return Path.Combine(directory,
            $"passes-{pullRequest.Owner}-{pullRequest.Repository}-{pullRequest.Number}-{stamp}.html");
    }

    private static void EnsurePrivateDirectory(string directory)
    {
        if (Directory.Exists(directory)) return;

        if (OperatingSystem.IsWindows())
            Directory.CreateDirectory(directory);
        else
            Directory.CreateDirectory(directory,
                UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
    }

    /// <summary>
    /// CreateNew: fails instead of overwriting, and never writes through an
    /// existing file or link at the target. Readable by the current user only
    /// on Unix, because the report contains source code.
    /// </summary>
    private static void WriteNew(string path, string html)
    {
        var options = new FileStreamOptions
        {
            Mode = FileMode.CreateNew,
            Access = FileAccess.Write,
            Share = FileShare.None
        };

        if (!OperatingSystem.IsWindows())
            options.UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;

        using var stream = new FileStream(path, options);
        using var writer = new StreamWriter(stream, new UTF8Encoding(false));
        writer.Write(html);
    }
}