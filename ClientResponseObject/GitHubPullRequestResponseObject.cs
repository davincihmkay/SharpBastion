using System.Text.Json.Serialization;

namespace SharpBastion.ClientResponseObject;

public class GitHubPullRequestResponseObject
{
    [JsonPropertyName("number")]
    public int Number { get; set; }

    [JsonPropertyName("state")]
    public string State { get; set; } = string.Empty;

    [JsonPropertyName("title")]
    public string Title { get; set; } = string.Empty;

    [JsonPropertyName("draft")]
    public bool Draft { get; set; }

    [JsonPropertyName("user")]
    public GitHubUserResponseObject? User { get; set; }

    [JsonPropertyName("head")]
    public GitHubBranchRefResponseObject Head { get; set; } = new();

    [JsonPropertyName("base")]
    public GitHubBranchRefResponseObject Base { get; set; } = new();

    /// <summary>
    /// True when the PR head lives in a different repository than its base.
    /// A deleted fork (null head repo) is treated as a fork — never pushable.
    /// </summary>
    public bool IsFromFork =>
        Head.Repository is null ||
        Base.Repository is null ||
        !string.Equals(Head.Repository.FullName, Base.Repository.FullName, StringComparison.OrdinalIgnoreCase);
}

public class GitHubBranchRefResponseObject
{
    [JsonPropertyName("ref")]
    public string Ref { get; set; } = string.Empty;

    [JsonPropertyName("sha")]
    public string Sha { get; set; } = string.Empty;

    [JsonPropertyName("repo")]
    public GitHubRepositoryResponseObject? Repository { get; set; }
}

public class GitHubRepositoryResponseObject
{
    [JsonPropertyName("full_name")]
    public string FullName { get; set; } = string.Empty;

    [JsonPropertyName("default_branch")]
    public string? DefaultBranch { get; set; }
}