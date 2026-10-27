namespace SharpBastion.RequestObject;

public class ExportPassesRequestObject
{
    /// <summary>Limit the export to one pull request. Null exports every recorded pass.</summary>
    public int? PullRequestNumber { get; set; }

    /// <summary>Output file. Null writes to the default exports directory.</summary>
    public string? OutputPath { get; set; }
}