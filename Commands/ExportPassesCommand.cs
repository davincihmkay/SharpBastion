using SharpBastion.RequestObject;

namespace SharpBastion.Commands;

public class ExportPassesCommand
{
    public int? PullRequestNumber;
    public bool PendingOnly;
    public string? OutputPath;

    public static ExportPassesCommand PopulateFromRequestObject(ExportPassesRequestObject requestObject)
    {
        if (requestObject.PullRequestNumber is <= 0)
            throw new ArgumentException($"Pull request number must be positive. Got: {requestObject.PullRequestNumber}.");

        string? outputPath = null;
        if (requestObject.OutputPath is not null)
        {
            outputPath = requestObject.OutputPath.Trim();

            if (outputPath.Length == 0)
                throw new ArgumentException("Output path cannot be empty.");

            var extension = Path.GetExtension(outputPath);
            if (!extension.Equals(".html", StringComparison.OrdinalIgnoreCase) &&
                !extension.Equals(".htm", StringComparison.OrdinalIgnoreCase))
                throw new ArgumentException("Output path must end in .html or .htm.");
        }

        return new ExportPassesCommand
        {
            PullRequestNumber = requestObject.PullRequestNumber,
            PendingOnly = requestObject.PendingOnly,
            OutputPath = outputPath
        };
    }
}