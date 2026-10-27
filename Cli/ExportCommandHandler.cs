using Microsoft.Extensions.DependencyInjection;
using SharpBastion.Controller;
using SharpBastion.RequestObject;

namespace SharpBastion.Cli;

/// <summary>
/// Console command handler for 'export': writes the recorded passes of a pull
/// request to an HTML report. Needs no GitHub token; it only reads the journal.
/// </summary>
public static class ExportCommandHandler
{
    private const string Usage =
        "Usage: export [--pr <number>] [--out <file.html>]   (--out takes the rest of the line, so put it last)";

    public static void Run(IServiceProvider hostProvider, string? argument)
    {
        if (!TryParse(argument, out var pullRequestNumber, out var outputPath))
        {
            Console.WriteLine(Usage);
            return;
        }

        using var scope = hostProvider.CreateScope();
        var controller = scope.ServiceProvider.GetRequiredService<PassExportController>();

        var result = controller.Export(new ExportPassesRequestObject
        {
            PullRequestNumber = pullRequestNumber,
            OutputPath = outputPath
        });

        Console.WriteLine(result.Success ? result.Message : $"[ERROR] {result.Message}");
    }

    private static bool TryParse(string? argument, out int? pullRequestNumber, out string? outputPath)
    {
        pullRequestNumber = null;
        outputPath = null;

        var rest = (argument ?? string.Empty).Trim();

        while (rest.Length > 0)
        {
            var split = rest.Split(' ', 2, StringSplitOptions.RemoveEmptyEntries);
            var flag = split[0];
            var tail = split.Length > 1 ? split[1].Trim() : string.Empty;

            if (flag.Equals("--pr", StringComparison.OrdinalIgnoreCase))
            {
                var valueSplit = tail.Split(' ', 2, StringSplitOptions.RemoveEmptyEntries);
                if (valueSplit.Length == 0 || !int.TryParse(valueSplit[0], out var parsed) || parsed <= 0)
                    return false;

                pullRequestNumber = parsed;
                rest = valueSplit.Length > 1 ? valueSplit[1].Trim() : string.Empty;
            }
            else if (flag.Equals("--out", StringComparison.OrdinalIgnoreCase))
            {
                if (tail.Length == 0) return false;

                // The rest of the line is the path, so spaces need no quoting.
                outputPath = ExpandHome(tail.Trim('"'));
                rest = string.Empty;
            }
            else
            {
                return false;
            }
        }

        return true;
    }

    private static string ExpandHome(string path) =>
        path.StartsWith("~/")
            ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), path[2..])
            : path;
}