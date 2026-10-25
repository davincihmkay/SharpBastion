using System.Text.Json.Serialization;

namespace SharpBastion.ClientResponseObject;

public class GitHubReviewCommentResponseObject
{
    [JsonPropertyName("id")]
    public long Id { get; set; }

    [JsonPropertyName("user")]
    public GitHubUserResponseObject? User { get; set; }

    /// <summary>OWNER, MEMBER, COLLABORATOR, CONTRIBUTOR, NONE, etc.</summary>
    [JsonPropertyName("author_association")]
    public string AuthorAssociation { get; set; } = string.Empty;

    [JsonPropertyName("path")]
    public string Path { get; set; } = string.Empty;

    /// <summary>Null when the commented line no longer exists at the current head (outdated).</summary>
    [JsonPropertyName("line")]
    public int? Line { get; set; }

    [JsonPropertyName("original_line")]
    public int? OriginalLine { get; set; }

    [JsonPropertyName("diff_hunk")]
    public string DiffHunk { get; set; } = string.Empty;

    [JsonPropertyName("body")]
    public string Body { get; set; } = string.Empty;

    [JsonPropertyName("in_reply_to_id")]
    public long? InReplyToId { get; set; }

    [JsonPropertyName("commit_id")]
    public string? CommitId { get; set; }

    [JsonPropertyName("html_url")]
    public string? HtmlUrl { get; set; }

    [JsonPropertyName("created_at")]
    public DateTimeOffset CreatedAt { get; set; }
}