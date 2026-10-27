using System.Text.Json.Serialization;

namespace SharpBastion.ClientResponseObject;

public class GitHubIssueCommentResponseObject
{
    [JsonPropertyName("id")]
    public long Id { get; set; }

    [JsonPropertyName("user")]
    public GitHubUserResponseObject? User { get; set; }

    [JsonPropertyName("author_association")]
    public string AuthorAssociation { get; set; } = string.Empty;

    [JsonPropertyName("body")]
    public string Body { get; set; } = string.Empty;

    [JsonPropertyName("html_url")]
    public string? HtmlUrl { get; set; }

    [JsonPropertyName("created_at")]
    public DateTimeOffset CreatedAt { get; set; }
}