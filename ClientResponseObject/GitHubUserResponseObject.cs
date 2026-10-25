using System.Text.Json.Serialization;

namespace SharpBastion.ClientResponseObject;

public class GitHubUserResponseObject
{
    [JsonPropertyName("login")]
    public string Login { get; set; } = string.Empty;

    [JsonPropertyName("type")]
    public string? Type { get; set; }
}