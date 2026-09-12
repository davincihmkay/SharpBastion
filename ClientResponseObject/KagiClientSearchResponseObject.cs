using System.Text.Json.Serialization;

namespace SharpBastion.ClientResponseObject;

public class KagiClientSearchResponseObject
{
    [JsonPropertyName("data")]
    public KagiSearchData? Data { get; set; }
}

public class KagiSearchData
{
    [JsonPropertyName("search")]
    public List<KagiSearchResultItem>? Search { get; set; }
}

public class KagiSearchResultItem
{
    [JsonPropertyName("url")]
    public string Url { get; set; } = string.Empty;

    [JsonPropertyName("title")]
    public string Title { get; set; } = string.Empty;

    [JsonPropertyName("snippet")]
    public string? Snippet { get; set; }
}