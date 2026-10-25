using System.Text.Json.Serialization;

namespace SharpBastion.ClientResponseObject;

public class GitHubErrorResponseObject
{
    [JsonPropertyName("message")]
    public string? Message { get; set; }
}