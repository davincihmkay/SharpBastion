using System.Text.Json.Serialization;

namespace SharpBastion.ClientResponseObject;

public class KagiErrorResponseObject
{
    [JsonPropertyName("error")]
    public List<KagiErrorDetail>? Error { get; set; }
}

public class KagiErrorDetail
{
    [JsonPropertyName("code")]
    public string? Code { get; set; }

    [JsonPropertyName("message")]
    public string? Message { get; set; }

    [JsonPropertyName("location")]
    public string? Location { get; set; }
}