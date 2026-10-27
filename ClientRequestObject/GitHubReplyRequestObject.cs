using System.Text.Json.Serialization;

namespace SharpBastion.ClientRequestObject;

[Serializable]
public class GitHubReplyRequestObject
{
    [JsonPropertyName("body")]
    public string Body { get; set; }

    public GitHubReplyRequestObject(string body)
    {
        Body = body;
    }
}